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
| Whole screen 2560×1440, desktop duplication (the picture the card hands over) | 2.0 ms | 0.6 / 10.3 |
| 400×300, desktop duplication (the picture not drawn again, so the one already held) | 0.1 ms | 0.1 / 0.1 |
| Whole screen 2560×1440, desktop (GDI) | 29.5 ms | 24.6 / 32.5 |
| A 400×300 patch, desktop (GDI) | 4.4 ms | 3.4 / 7.2 |
| Whole screen 2560×1440, graphics capture (the library way, used when the display cannot be duplicated) | 18 ms | 13 / 24 |
| The same, a session opened and closed per read | 51 ms | 48 / 54 |
| One window, window (PrintWindow) | 21 ms | 20 / 26 |
| One 800×600 window, window (graphics capture) | 10 ms | 8 / 11 |

The cheapest way to read the whole screen is **desktop duplication** (DXGI Desktop Duplication): the
picture the graphics card is composing for the monitor is duplicated into a texture of our own and
read out of it, so what comes back is what is on screen. A 2560×1440 frame costs 2 ms, more than ten
times less than copying the screen with GDI, and it is the only way to read a game that fills the
screen on its own (GDI answers with black). It is also the only way that gets a picture for nothing:
a desktop that has not been drawn again has no new frame, and the picture already held is what is on
screen, so asking again costs 0.1 ms. What it costs is one graphics device per process, built the
first time it is asked for (about 0.25 s, once per run), and it cannot read a display showing high
dynamic range or one turned on its side: then the whole-screen way falls back on the library's
graphics capture (18 ms, the row below) and, failing that, on GDI.

What a GDI capture costs follows how much is being captured: the whole screen copies
2560×1440×4 ≈ 14.7 MB out of the display device and a 400×300 patch is a fifteenth of that; almost
all of those 29 ms are that copy (measured on its own, the `BitBlt` is about 16 ms, getting the
pixels into managed memory 0.6 ms and allocating the buffer 0.6 ms). Desktop duplication is
indifferent to how much is being captured — it takes the whole display and cuts the part that was
asked for out of it, so 400×300 costs what the whole screen costs (2 ms, or 0.1 ms when nothing has
been drawn). For a small patch, GDI at 4 ms is still the cheapest.

**A window's graphics capture session has to be kept**: opening one — building the graphics device
and the pool of pictures — takes 0.4 s the first time, and after that a frame is only a copy from
graphics memory. A kept session therefore reads a window in 10 ms and the whole screen in 18 ms,
while opening and closing one per read costs 51 ms, about three times as much. Windows also draws a
capture border around a window being read, which is there for as long as a session is: **opening and
closing one per read makes that border blink**, which is a second reason to keep it. Windows the
library cannot read at all — layered windows, the Settings pages, the input-method candidates, the
graphics driver's overlay — are tried once and then remembered, so no session is opened and thrown
away for them over and over.

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
| Colour that is not on the screen at all (whole picture walked) | 2.5 ms |
| Colour, the 20 closest ones (whole picture walked) | 11.9 ms |
| Colour with its coverage counted, whole screen (walked twice: once to find, once to count) | 4.3 ms |
| Colour with its coverage counted, 400×300 | 0.2 ms |
| Colour pattern, 400×300 (three points, the colour nowhere on the picture) | 0.2 ms |
| Whether the picture moved (one round of waiting for stillness) | 5.7 ms |
| Text recognition, whole screen | 128 ms |
| Text recognition, 800×200 | 23 ms |

- What finding a picture costs follows "area searched × area of the reference": 211 ms for a 200×80
  in the whole screen becomes 14 ms for the same picture inside 640×480, a fifteenth. **Making the
  region smaller is the biggest lever there is**, bigger than changing the algorithm and bigger than
  capturing one time fewer.
- Finding a colour looks at single pixels: the earlier the hit turns up, the cheaper it is (a hit on
  the first pixel measured as 0 ms); "the ones closest to the colour" has to walk the whole picture,
  which is 12 ms. A search that is going to find nothing costs 2.5 ms. **What decides how dear one
  pixel is to compare is whether a square root has to be taken for it**: a colour is compared by the
  sum of its channels' squared differences, which is the same question as comparing the distance, and
  the square root of that distance was being paid once per pixel. Leaving it out makes the same walk
  about six times faster (2.5 ms against 14.5 ms). "The ones closest to the colour" orders by the
  squared difference, which puts them in the same order as their distance does, so the dearest row of
  the table came down from 30 ms to 12 ms as well.
- Counting the coverage and finding a colour pattern both walk the whole area: 2.5 ms for the whole
  2560×1440 screen, 4.3 ms when it finds first and counts afterwards, and 0.2 ms for 400×300. The
  condition that asks for a colour share walks the same way, so it costs what asking a colour search
  for its coverage costs. **The coverage is only counted by a step that asks for it**, so a step that
  does not ask still takes the "stop at the first hit" path.
- The first text recognition in a process spends 1.1–10.3 s reading the model in (how hot the disk
  cache is decides which end of that), and is 128 ms after that. Whole-screen text recognition is the
  most expensive single look in the table.

## What one look adds up to

One look = one capture + one algorithm run over it. With the defaults (`frameMaxAgeMs` 0 and
`intervalMs` 0) every look captures again, so:

| What the step does | One look | Times a second |
| --- | --- | --- |
| Find a 200×80 picture in the whole screen | 2.0 + 211 ≈ 213 ms | about 5 |
| The same, inside 640×480 only | 4.4 + 14 ≈ 18 ms | about 55 |
| Find a colour in the whole screen (hit early) | 2.0 + 0.4 ≈ 2.4 ms | about 400 |
| Recognise text over the whole screen | 2.0 + 128 ≈ 130 ms | about 8 |
| One round of waiting for the picture to hold still | 2.0 + 5.7 ≈ 8 ms | about 125 |

Two switches change that number. Both are on the **Advanced** page and both are off by default:

- **Accept a picture up to** (`frameMaxAgeMs`): above 0, one capture serves every step reading the
  same source the same way within that age, and the later steps cut their own rectangle out of it.
  Ten steps reading the same part of the screen in a row take the capture cost from 10 × 29.5 ≈ 295 ms
  down to 30 ms on the GDI way, and from 10 × 2 ≈ 20 ms down to 2 ms on the duplication way.
- **Time between looks** (`intervalMs`): the least time one step leaves between two looks at the
  screen. A step that waits for something uses it as its polling pace, and left empty that is one
  screen refresh — 16 ms on a 60 Hz display, 5 ms on a 240 Hz one — so one round of polling costs
  the row above times the number of rounds. The refresh rate is the floor under looking at all:
  until the picture is drawn again there is nothing new to read.

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
