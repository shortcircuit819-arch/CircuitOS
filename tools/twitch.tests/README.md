# Twitch regression tests

Run `dotnet run --project tools/twitch.tests/CircuitOS.Twitch.Tests.csproj` from the repository root.

The executable links the production runtime sources and uses in-memory HTTP/WebSocket transports. It tests shared one-use token refresh, logout/account isolation, mandatory subscription recovery, reconnect handover ordering, and safe redemption refunds. Persistence tests use real local stores and encrypted token files in unique temporary directories, which are removed afterward. No Twitch account, live data, or network connection is used.

The project uses the same ProtectedData package already required by the runtime and smoke suite.
