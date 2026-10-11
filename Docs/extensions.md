# Editing extensions and MCP control

## Tile selection and clipboard

Shift-click selects an inclusive rectangle from the previous cursor cell. Further
Shift-clicks extend from the same anchor. Alt-click toggles an individual tile.
Ctrl/Cmd+A selects all tiles; Escape clears the selection. Shift+arrow keys select
a rectangle from a keyboard anchor. An ordinary click clears the selection.

Ctrl/Cmd+C copies the selection, or the cursor's sub-stack when nothing is selected.
Ctrl/Cmd+V pastes at the cursor. Copy and Paste buttons are also on the Tile panel.
The clipboard is internal to Jacquard and survives a score load during the session.
Each paste makes independent tile instances.

Group copies preserve grid offsets. Destinations must be empty steps or stack
bottoms on existing lanes; a terminator can grow if there is room. A group that
cannot fit is rejected whole. Holes in a vertical selection cannot create holes
in a stack. Flow tiles, including jumps, have no independent copy and are skipped,
as with the original stack clipboard. Delete/Backspace deletes the selection, or
the cursor tile/lane when there is no selection. Deleting a jump removes its branch.

Tab or Fold / Unfold toggles the cursor's stack. A +count marker shows hidden
tiles, and the top tile's tooltip reports the total count. Folding hides lower tile elements without changing grid spacing,
playback or saved files. A score load clears selection and folding.

Existing arrow movement, Enter audition and Space transport remain available.
Ctrl/Cmd+S saves the current score slot. Keyboard shortcuts apply while the score
plane has focus. Pending score loads lock editing as before.

## Timbre presets

Select a CHAN head to find Sine, Hollow, Bass, Bell, Keys, Pad, Kick and Metal above
the sound controls. A preset replaces the channel's complete FM patch, including
transpose, level, stereo settings and sends. Afterwards, edit individual parameters
as usual. The existing score format persists the resulting values. Presets are
starting values and have not been auditioned in this environment.

## MCP setup

The MCP server is a standalone .NET 10 console application with no NuGet package
dependencies. It implements newline-delimited JSON-RPC over stdio, initialization,
ping, tools/list and tools/call. Protocol versions 2024-11-05, 2025-03-26,
2025-06-18 and 2025-11-25 are supported. Unknown requested versions negotiate
2025-11-25. See the [MCP transport specification](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports)
and [tool specification](https://modelcontextprotocol.io/specification/2025-11-25/server/tools).

Build the server:

```powershell
dotnet build Tools/Jacquard.Mcp/Jacquard.Mcp.csproj -c Release
```

Launch a desktop Jacquard build containing these changes with both environment
variables set. For an editor session, start Unity from the same environment:

```powershell
$env:JACQUARD_CONTROL_PORT = '32145'
$env:JACQUARD_CONTROL_TOKEN = [guid]::NewGuid().ToString('N')
& 'C:\path\to\Jacquard.exe'
```

Keep the generated token and use the same value in the MCP client's environment.
The bridge remains disabled if the port variable is absent. A port must be in
1024..65535 and a token must have at least 32 characters. The listener binds to
IPv4 loopback only. The MCP server is stdio; the internal TCP bridge is a separate
app protocol and is not an MCP network endpoint. WebGL and mobile players do not
include the bridge.

Configure the client using absolute paths and your generated token:

```json
{
  "mcpServers": {
    "jacquard": {
      "command": "dotnet",
      "args": ["C:\\path\\to\\Jacquard\\Tools\\Jacquard.Mcp\\bin\\Release\\net10.0\\Jacquard.Mcp.dll"],
      "env": {
        "JACQUARD_CONTROL_PORT": "32145",
        "JACQUARD_CONTROL_TOKEN": "replace-with-the-token-used-to-launch-Jacquard"
      }
    }
  }
}
```

The tools are `jacquard_state`, `jacquard_presets`, `jacquard_play`,
`jacquard_stop`, `jacquard_tempo`, `jacquard_preset`, `jacquard_cursor`,
`jacquard_select`, `jacquard_select_all`, `jacquard_clear_selection`,
`jacquard_copy`, `jacquard_paste`, `jacquard_delete`, `jacquard_fold`,
`jacquard_note` and `jacquard_save`. Tools/list supplies their argument schemas.
Read state before editing: coordinates refer to the current plane, which can
reframe when edits change its extent. The state tool returns the canonical score
text, transport state, cursor and selection count. Save writes the selected score
slot; edits otherwise stay in memory.

Control requests are dispatched on the Unity main thread. The bridge has a
five-second queue deadline and the client has an eight-second connection timeout.
A timed-out command that had not started is discarded. A lost response after an
edit started leaves its outcome uncertain; read state before retrying a mutation.

## Verification and limits

The .NET tooling can be checked without Unity:

```powershell
dotnet build Tools/CoreCheck/CoreCheck.csproj
dotnet build Tools/Jacquard.Mcp/Jacquard.Mcp.csproj
dotnet run --project Tools/SyntaxCheck/SyntaxCheck.csproj -- .
```

CoreCheck compiles the Unity-independent model, serializer, sequencer and synth
settings. SyntaxCheck parses all Assets C# files as C# 9 with default, editor,
standalone desktop, WebGL, Android and iOS symbols. It uses Roslyn bundled with the installed SDK.
Parsing verifies syntax, not Unity API binding, IL2CPP compatibility, interaction,
sound or a real MCP session. No Unity build or runtime tests were performed.

The sampling instrument request is deferred. It would require a sample store,
platform-specific import UI, a sampler audio renderer and persistent sample
references, rather than converting a file into an FM note tile. This extension
does not claim to play uploaded recordings.

Development branches are cumulative: `feature/tile-groups`,
`feature/timbre-presets`, `feature/mcp-control`, then
`feature/shortcuts-and-validation`. `feature/jacquard-extensions` contains the
combined result. All branches use the requested fork as origin.
