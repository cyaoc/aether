using Aether.Core;

namespace Aether.Gui;

public sealed record PoolDrawRow(string Viewer, bool StoppedByHost, Grade Grade, string Prize, string Time, bool Fresh);
