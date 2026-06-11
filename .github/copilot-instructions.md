# Copilot Instructions

## 项目指南
- This project is on the Equipment side: it collects device information, converts it to SECS messages for Host, and also receives Host data and translates it into internal data for gRPC communication. 
- Define the minimal production flow: report mapping results first; Host then issues control commands in sequence `S3F17`, `S16F15`, `S14F9`, `S2F41`, etc.; device reports test results via `S6F11`.
- Persist both alarm events and key operational logs into the database for traceability, separated by Equipment-side, Host-side, and middleware-side critical information.

## gRPC Method Organization
- `efem.proto` should contain all gRPC receiving (server-side) methods.
- `secs.proto` should contain all proactively outgoing (client-side) methods.

## Architectural Preferences
- Keep `CommunicationPrimaryMessageHandler` as the orchestration/state layer.
- Centralize concrete `HandleSxFyAsync` implementations in shared function files reused by handlers.

## Code Comments
- Include key comments at branch points (methods, if/for/switch, and other critical decision points) in future code changes to enhance clarity and maintainability.
- Default to making code modifications with each inquiry rather than just analysis.
- For every new or modified method, add comments explaining its functionality and purpose.
- All asynchronous method calls (including `await` statements) must also include comments explaining their purpose and functionality.