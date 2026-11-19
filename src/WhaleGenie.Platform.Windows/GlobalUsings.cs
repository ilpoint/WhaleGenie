// The engine's contracts and data shapes. The adapters sit below them, so every file in this
// project reads the same few namespaces; writing them once here keeps each adapter about the
// Windows API it wraps rather than about where its types live.
global using WhaleGenie.Core.Devices;
global using WhaleGenie.Core.Execution;
global using WhaleGenie.Core.Expressions;
global using WhaleGenie.Core.Recording;
global using WhaleGenie.Core.Variables;
global using WhaleGenie.GraphicsCapture;
