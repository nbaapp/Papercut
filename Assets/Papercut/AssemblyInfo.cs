using System.Runtime.CompilerServices;

// The EditMode tests probe internal runtime seams (e.g. TerrainFill.Refresh and its hidden part) headlessly.
[assembly: InternalsVisibleTo("Papercut.Tests.EditMode")]
