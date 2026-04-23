# Copilot Instructions

## 项目指南
- This project is on the Equipment side: it collects device information, converts it to SECS messages for Host, and also receives Host data and translates it into internal data for gRPC communication.

## gRPC Method Organization
- `efem.proto` should contain all gRPC receiving (server-side) methods.
- `secs.proto` should contain all proactively outgoing (client-side) methods.

## Architectural Preferences
- Keep `CommunicationPrimaryMessageHandler` as the orchestration/state layer.
- Centralize concrete `HandleSxFyAsync` implementations in shared function files reused by handlers.