# Latest Code Scan Summary (2026-03-02)

Scanned 1 files | High: 1 | Medium: 0 | Low: 0

## Top Issues

- **HIGH** `NetworkDialogueService.cs` — [multiplayer_safety] PlayerNetworkId is a NetworkObject identifier, but the field is not marked with [SyncVar] or used in conjunction with NetworkVariable<T> to ensure synchronization across clients. This could lead to desynchronization issues if the value changes on the server without being properly synced.

Full report: `dev_tools/reports/code_scan_2026-03-02.md`