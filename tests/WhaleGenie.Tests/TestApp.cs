using Avalonia.Headless;

// The headless session builds the application from WhaleGenie.App, so the tests work on a machine
// with no desktop and never touch the real screen, keyboard or mouse.
[assembly: AvaloniaTestApplication(typeof(WhaleGenie.App))]
