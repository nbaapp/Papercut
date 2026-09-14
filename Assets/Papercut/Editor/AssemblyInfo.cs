using System.Runtime.CompilerServices;

// The EditMode tests probe internal editor seams (e.g. the fold pane's press-priority chain) headlessly.
[assembly: InternalsVisibleTo("Papercut.Tests.EditMode")]
