using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using CircuitOS.Runtime;

var failed = 0;
foreach (var (name, test) in new (string, Func<Task>)[] {
    ("single-use refresh shared between runtime and admin", TokenRefresh),
    ("logout invalidates stale sessions without affecting bot", Logout),
    ("account change invalidates prior session", AccountChange),
    ("mandatory subscription retries after transient failure", SubscriptionRetry),
    ("network cancellation retries while app remains running", NetworkCancellation),
    ("reconnect retains old socket until replacement welcome", ()=>Reconnect(false)),
    ("immediate replacement welcome drains buffered old events", ()=>Reconnect(true)),
    ("old peer closes before replacement welcome", ()=>Reconnect(false,true)),
    ("pre-write engine failure refunds managed reward", Refund),
    ("post-commit fulfillment failure never refunds", FulfillmentFailure),
    ("ambiguous inventory write failure never refunds", WriteFailure),
    ("attach-only failure never attempts refund", AttachOnlyFailure)
}) { try { await test(); Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); } }
return failed == 0 ? 0 : 1;

static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
static string Temp() { var p=Path.Combine(Path.GetTempPath(), "CircuitOS-twitch-tests-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(p); return p; }
static TwitchTokens Tokens(bool expired=false)=>new("test-access","first-refresh",DateTimeOffset.UtcNow.AddHours(expired ? -1 : 4),"test-user","test-user","Test User");
static TwitchOptions Options()=>new("test-public-client","",TwitchOptions.DefaultRedirectUri);
static async Task TokenRefresh() {
    var root=Temp();
    try {
        var calls=0;
        using var http=new HttpClient(new FakeHttp(r=>{
            calls++;
            return calls==1 ? Reply(200,"""{"access_token":"fresh-access","refresh_token":"second-refresh","expires_in":14400}""") : Reply(400,"""{"message":"Invalid refresh token"}""");
        }));
        var tokens=Tokens(true); tokens.Save(root);
        var runtime=new TwitchSession(Options(),tokens,root,http:http);
        var admin=new TwitchSession(Options(),tokens,root,http:http);
        var values=await Task.WhenAll(Task.Run(()=>runtime.AccessToken),Task.Run(()=>admin.AccessToken));
        Check(values.All(v=>v=="fresh-access") && calls==1,"Both sessions must share exactly one refresh");
        Check(TwitchTokens.TryLoad(root)?.RefreshToken=="second-refresh","Rotated token must persist");
    } finally { Directory.Delete(root,true); }
}
static Task Logout() {
    var root=Temp();
    try {
        var tokens=Tokens(); tokens.Save(root); tokens.Save(root,TwitchTokens.BotFileName);
        var session=new TwitchSession(Options(),tokens,root);
        var bot=new TwitchSession(Options(),tokens,root,TwitchTokens.BotFileName);
        TwitchSession.ClearTokens(root);
        foreach(var stale in new[]{session,new TwitchSession(Options(),tokens,root)}) {
            var rejected=false;try{_ = stale.AccessToken;}catch(InvalidOperationException){rejected=true;}
            Check(rejected,"Logged-out token snapshot was resurrected");
        }
        Check(!File.Exists(Path.Combine(root,TwitchTokens.FileName)),"Logout did not remove token file");
        Check(bot.AccessToken==tokens.AccessToken,"Broadcaster logout cleared bot tokens");
    } finally {Directory.Delete(root,true);}
    return Task.CompletedTask;
}
static Task AccountChange() {
    var root=Temp();
    try {
        var tokens=Tokens();tokens.Save(root);var old=new TwitchSession(Options(),tokens,root);
        var next=tokens with{UserId="other",AccessToken="other-access"};next.Save(root);
        var rejected=false;try{_ = old.AccessToken;}catch(InvalidOperationException){rejected=true;}
        Check(rejected,"Old account session used new account credentials");
        Check(new TwitchSession(Options(),next,root).AccessToken=="other-access","New login unavailable");
    } finally {Directory.Delete(root,true);}
    return Task.CompletedTask;
}static async Task SubscriptionRetry() {
    var root=Temp(); using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(3));
    try {
        var tries=0; var connects=0; var delivered=0;
        using var http=new HttpClient(new FakeHttp(r=>{
            if(r.Content!.ReadAsStringAsync().Result.Contains("redemption.add") && ++tries==1) return Reply(503,"{}");
            return Reply(202,"{}");
        }));
        var session=new TwitchSession(Options(),Tokens(),root);
        Task<WebSocket> Connect(Uri u,CancellationToken c) {
            var socket=new FakeSocket(); socket.Push(Welcome("session"+(++connects)));
            if(connects>1) socket.Push(Notification("retry-success"));
            return Task.FromResult<WebSocket>(socket);
        }
        var listener=new TwitchEventSub(session,new TwitchHelix(session,http:http),_=>{delivered++;cancel.Cancel();},_=>{},_=>{},Connect,TimeSpan.Zero);
        await listener.RunAsync(cancel.Token);
        Check(tries>=2 && delivered==1,"Transient redemption subscription failure left socket chat-only (attempts="+tries+")");
    } finally { Directory.Delete(root,true); }
}
static async Task NetworkCancellation() {
    var root=Temp(); using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(3));
    try {
        var connects=0;var delivered=0;
        using var http=new HttpClient(new FakeHttp(_=>Reply(202,"{}")));
        var session=new TwitchSession(Options(),Tokens(),root);
        Task<WebSocket> Connect(Uri uri,CancellationToken token) {
            if(++connects==1)return Task.FromException<WebSocket>(new OperationCanceledException("Simulated network timeout"));
            var socket=new FakeSocket();socket.Push(Welcome("recovered"));socket.Push(Notification("recovered"));return Task.FromResult<WebSocket>(socket);
        }
        var listener=new TwitchEventSub(session,new TwitchHelix(session,http:http),_=>{delivered++;cancel.Cancel();},null,_=>{},Connect,TimeSpan.Zero);
        await listener.RunAsync(cancel.Token);
        Check(connects==2 && delivered==1,"Network cancellation stopped listener even though app was not canceled");
    } finally {Directory.Delete(root,true);}
}static async Task Reconnect(bool immediate, bool peerCloses=false) {
    var root=Temp(); using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(3));
    try {
        var subscriptions=0; var connects=0; var delivered=0;
        using var http=new HttpClient(new FakeHttp(_=>{subscriptions++;return Reply(202,"{}");}));
        var session=new TwitchSession(Options(),Tokens(),root);
        var old=new FakeSocket();old.Push(Welcome("old"));old.Push("""{"metadata":{"message_id":"reconnect","message_type":"session_reconnect"},"payload":{"session":{"reconnect_url":"wss://replacement.test/ws"}}}""");
        var oldAlive=false;
        var handoverReady=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<WebSocket> Connect(Uri u,CancellationToken c) {
            if(++connects==1)return old;
            oldAlive=!old.Disposed;
            old.Push(Notification("during-handover"));
            if (!immediate) await handoverReady.Task.WaitAsync(c);
            if(peerCloses){old.Push("close-frame");await old.CloseObserved.Task.WaitAsync(c);}
            var next=new FakeSocket();next.Push(Welcome("next"));next.Push(Notification("after-move"));
            return next;
        }
        var listener=new TwitchEventSub(session,new TwitchHelix(session,http:http),_=>{if(++delivered==1) handoverReady.TrySetResult(); else cancel.Cancel();},_=>{},_=>{},Connect,TimeSpan.Zero);
        await listener.RunAsync(cancel.Token);
        Check(oldAlive,"Old socket disposed before replacement connected");
        Check(delivered==2 && subscriptions==2,"Transferred subscriptions must not be recreated");
    } finally { Directory.Delete(root,true); }
}
static Task Refund()=>RedemptionCase(false);
static Task FulfillmentFailure()=>RedemptionCase(true);
static Task WriteFailure()=>RedemptionCase(true, writeFailure:true);
static Task AttachOnlyFailure()=>RedemptionCase(false, manageable:false);
static Task RedemptionCase(bool valid, bool writeFailure=false, bool manageable=true) {
    var root=Temp();
    try {
        var store=new LocalFileDataStore(root);
        store.WriteProfileData("default",DataKeys.Profile,JsonNode.Parse("""{"gameName":"Test","messages":{"success":"{item}"}}""")!);
        store.WriteProfileData("default",DataKeys.Catalog,JsonNode.Parse(valid ? """{"collections":{"test":{"displayName":"Test","weight":1,"parts":[{"id":"item","name":"Item"}]}}}""" : """{"collections":{}}""")!);
        store.WriteProfileData("default",DataKeys.Inventory,new JsonObject());
        using var fileLock = writeFailure ? new FileStream(Path.Combine(store.DataPath,"inventory.json"),FileMode.Open,FileAccess.Read,FileShare.Read) : null;
        var statuses=new List<string>();
        using var http=new HttpClient(new FakeHttp(r=>{
            statuses.Add(JsonNode.Parse(r.Content!.ReadAsStringAsync().Result)!["status"]!.ToString());
            return Reply(valid?503:200,"{}");
        }));
        var session=new TwitchSession(Options(),Tokens(),root);
        TwitchRuntime.HandleRedemption(new("reward","Reward","redeem","viewer","Viewer"),new Dictionary<string,TwitchRuntime.RewardRoute>{{"reward",new("default",manageable)}},new CircuitService(store),new TwitchHelix(session,http:http),_=>{});
        Check(statuses.SequenceEqual(writeFailure || !manageable ? Array.Empty<string>() : new[]{valid?"FULFILLED":"CANCELED"}),"Unexpected redemption updates: "+string.Join(",",statuses));
        if(valid && !writeFailure)Check(store.ReadProfileDataStrict("default",DataKeys.Inventory)?["viewer"]?["components"]?["item"]?.GetValue<int>()==1,"Committed pull was lost");
        else Check(store.ReadProfileDataStrict("default",DataKeys.Inventory)?["viewer"] is null,"Failed pull changed inventory");
    } finally { Directory.Delete(root,true); }
    return Task.CompletedTask;
}
static HttpResponseMessage Reply(int code,string body)=>new((HttpStatusCode)code){Content=new StringContent(body)};
static string Welcome(string id)=>new JsonObject{["metadata"]=new JsonObject{["message_id"]="welcome-"+id,["message_type"]="session_welcome"},["payload"]=new JsonObject{["session"]=new JsonObject{["id"]=id,["keepalive_timeout_seconds"]=10}}}.ToJsonString();
static string Notification(string id)=>new JsonObject{["metadata"]=new JsonObject{["message_id"]=id,["message_type"]="notification",["subscription_type"]="channel.channel_points_custom_reward_redemption.add"},["payload"]=new JsonObject{["event"]=new JsonObject{["id"]=id,["user_id"]="viewer",["reward"]=new JsonObject{["id"]="reward"}}}}.ToJsonString();
sealed class FakeHttp(Func<HttpRequestMessage,HttpResponseMessage> send):HttpMessageHandler {
    protected override HttpResponseMessage Send(HttpRequestMessage request,CancellationToken c)=>send(request);
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken c)=>Task.FromResult(send(request));
}
sealed class FakeSocket:WebSocket {
    readonly Channel<string> messages=Channel.CreateUnbounded<string>();readonly CancellationTokenSource closed=new();
    public bool Disposed; WebSocketState state=WebSocketState.Open; public readonly TaskCompletionSource CloseObserved=new(TaskCreationOptions.RunContinuationsAsynchronously); public void Push(string text)=>messages.Writer.TryWrite(text);
    public override WebSocketCloseStatus? CloseStatus=>null;public override string? CloseStatusDescription=>null;public override string? SubProtocol=>null;public override WebSocketState State=>Disposed?WebSocketState.Closed:state;
    public override void Abort(){closed.Cancel();}public override void Dispose(){Disposed=true;closed.Cancel();}
    public override Task CloseAsync(WebSocketCloseStatus s,string? d,CancellationToken c){Dispose();return Task.CompletedTask;}
    public override Task CloseOutputAsync(WebSocketCloseStatus s,string? d,CancellationToken c){if(state==WebSocketState.CloseReceived)state=WebSocketState.Closed;else{state=WebSocketState.CloseSent;messages.Writer.TryWrite("close-frame");}return Task.CompletedTask;}
    public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer,CancellationToken c){if(State==WebSocketState.Closed)throw new InvalidOperationException("Read on closed socket");using var linked=CancellationTokenSource.CreateLinkedTokenSource(c,closed.Token);var text=await messages.Reader.ReadAsync(linked.Token);if(text=="close-frame"){state=state==WebSocketState.CloseSent?WebSocketState.Closed:WebSocketState.CloseReceived;CloseObserved.TrySetResult();return new(0,WebSocketMessageType.Close,true);}var bytes=Encoding.UTF8.GetBytes(text);bytes.CopyTo(buffer.Array!,buffer.Offset);return new(bytes.Length,WebSocketMessageType.Text,true);}
    public override Task SendAsync(ArraySegment<byte> b,WebSocketMessageType t,bool e,CancellationToken c)=>throw new NotSupportedException();
}
