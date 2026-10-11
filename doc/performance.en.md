# Performance

[English](performance.en.md) | [简体中文](performance.md)

What one look at the screen costs, on which machine these numbers were taken, and how. It is not part
of CI: after changing how the screen is read (`WindowsScreenDevice`) or how it is matched and read as
text (`OpenCvVisionDevice`, `PixelSearch`, `PaddleOcrDevice`), leave a new entry here so it can be
compared with the one before it. For who reads the screen and when, see "Where the picture comes
from" in [README.md](../README.md).

## How the numbers were taken

- Machine: 2560×1440 desktop, 16 cores, Windows, the `net10.0-windows10.0.19041` Release build.
- Method: a throwaway project referencing `WhaleGenie.Core` and driving the engine's own devices —
  `WindowsDeviceLayer`'s `Screen`, `Vision` and `Ocr`, which is the same path a run takes. Every
  operation was warmed up once and then measured 6–15 times, and the median is what is written down.
  The project was deleted once the numbers were in and is not kept in the repository.
- The reference picture is a 200×80 patch cut out of the screen just captured, so it is always
  found: these are the costs of a search that succeeds.
- These are magnitudes for one machine. Another resolution, graphics card or CPU moves them. What is
  worth remembering is the ratios between them.

## Taking a picture

| How | Median | Fastest / slowest |
| --- | --- | --- |
| Whole screen 2560×1440, desktop (GDI) | 29.5 ms | 24.6 / 32.5 |
| A 400×300 patch, desktop (GDI) | 4.4 ms | 3.4 / 7.2 |
| Whole screen 2560×1440, whole screen (graphics capture) | 63 ms | 60 / 70 |
| One window, window (PrintWindow) | 21 ms | 20 / 26 |
| One window, window (graphics capture) | 19 ms | 14 / 21 |

What a capture costs follows how much is being captured: the desktop way copies 2560×1440×4 ≈ 14.7 MB
out of the display device, and a 400×300 patch is a fifteenth of that. Graphics capture costs about
twice as much and buys reading a window that another window is covering.

## Finding something in the picture

A 200×80 reference picture, the picture being the whole 2560×1440 screen except where noted:

| What is looked for | Median |
| --- | --- |
| Picture, normalised correlation (`Normed`) | 211 ms |
| Picture, cross correlation (`Correlated`) | 202 ms |
| Picture, square difference (`Difference`) | 201 ms |
| Picture, feature matching (`Feature`) | 281 ms |
| Picture, in a 640×480 patch only (`Normed`) | 14 ms |
| Colour, appearing near the start of the picture | ~0 ms |
| Colour that is not on the screen at all (whole picture walked) | 16.7 ms |
| Colour, the 20 closest ones (whole picture walked) | 29.8 ms |
| Colour with its coverage counted, whole screen (walked twice: once to find, once to count) | 16.6 ms |
| Colour with its coverage counted, 400×300 | 0.6 ms |
| Colour pattern, 400×300 (three points, the colour nowhere on the picture) | 0.6 ms |
| Whether the picture moved (one round of waiting for stillness) | 5.7 ms |
| Text recognition, whole screen | 128 ms |
| Text recognition, 800×200 | 23 ms |

- What finding a picture costs follows "area searched × area of the reference": 211 ms for a 200×80
  in the whole screen becomes 14 ms for the same picture inside 640×480, a fifteenth. **Making the
  region smaller is the biggest lever there is**, bigger than changing the algorithm and bigger than
  capturing one time fewer.
- Finding a colour looks at single pixels: the earlier the hit turns up, the cheaper it is (a hit on
  the first pixel measured as 0 ms); "the ones closest to the colour" has to walk the whole picture,
  which is 30 ms. A search that is going to find nothing costs 17 ms.
- Counting the coverage and finding a colour pattern both walk the whole area: 16.6 ms for the whole
  2560×1440 screen, the same order as a colour search that walks the whole picture, and 0.6 ms for
  400×300. **The coverage is only counted by a step that asks for it**, so a step that does not ask
  still takes the "stop at the first hit" path.
- The first text recognition in a process spends 1.1–10.3 s reading the model in (how hot the disk
  cache is decides which end of that), and is 128 ms after that. Whole-screen text recognition is the
  most expensive single look in the table.

## What one look adds up to

One look = one capture + one algorithm run over it. With the defaults (`frameMaxAgeMs` 0 and
`intervalMs` 0) every look captures again, so:

| What the step does | One look | Times a second |
| --- | --- | --- |
| Find a 200×80 picture in the whole screen | 29.5 + 211 ≈ 240 ms | about 4 |
| The same, inside 640×480 only | 29.5 + 14 ≈ 43 ms | about 23 |
| Find a colour in the whole screen (hit early) | 29.5 + 0 ≈ 30 ms | about 33 |
| Recognise text over the whole screen | 29.5 + 128 ≈ 158 ms | about 6 |
| One round of waiting for the picture to hold still | 29.5 + 5.7 ≈ 35 ms | about 28 |

Two switches change that number. Both are on the **Advanced** page and both are off by default:

- **Accept a picture up to** (`frameMaxAgeMs`): above 0, one capture serves every step reading the
  same source the same way within that age, and the later steps cut their own rectangle out of it.
  Ten steps reading the same part of the screen in a row take the capture cost from 10 × 29.5 ≈ 295 ms
  down to 30 ms.
- **Time between looks** (`intervalMs`): the least time one step leaves between two looks at the
  screen. A step that waits for something uses it as its polling pace, 50–200 ms by default, so one
  round of polling costs the row above times the number of rounds.

## What is not there yet

- The throttle is per step (`MacroRunner._looked` is keyed by step id). There is no "this macro looks
  at the screen at most N times a second" over the whole macro: ten different find-picture steps in
  one loop each go as fast as they can, and the total is their sum. Making them share a picture takes
  `frameMaxAgeMs` on every one of them, and what is shared is keyed by "source + way of capturing", so
  changing `captureMode` shares nothing.
- Capturing and matching are done on the running thread, so those 29 ms are 29 ms of blocking. A macro
  is a sequence anyway, so it only makes that round longer; it does not disturb other steps.
- The text recognition model is read in once per process, paid by the first look.

## Taking the numbers again

Build a throwaway project the way "How the numbers were taken" describes, referencing
`WhaleGenie.Core` and taking the devices from `WindowsDeviceLayer`:

```csharp
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;

var layer = new WindowsDeviceLayer();
var whole = layer.Screen.Capture(
    new ScreenCaptureRequest(0, 0, 2560, 1440, CaptureMethod.Gdi));

var needle = Cut(whole.Frame, 900, 500, 200, 80);   // cut out of the picture just taken
var hits = layer.Vision.FindAll(whole.Frame, needle,
    new VisionQuery(MatchAlgorithm.Normed, 80, 6, null, 1));

var spans = layer.Ocr.Recognize(whole.Frame, "auto");
```

Stop whatever else is doing real work first (above all, do not run a whole-solution `dotnet format`
beside it), and take medians rather than single runs.
