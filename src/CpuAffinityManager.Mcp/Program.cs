using CpuAffinityManager.Mcp;

// CPU Affinity Manager — MCP Server
// Provides AI-agent-accessible tools via Model Context Protocol (stdio transport).
//
// Usage: CpuAffinityManager.Mcp.exe
//   Reads JSON-RPC from stdin, writes responses to stdout.
//   All logging goes to stderr to avoid corrupting the JSON stream.

var server = new McpServer();
await server.RunAsync();
