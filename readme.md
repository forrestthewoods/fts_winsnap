fts_winsnap
===

fts_winsnap is an AeroSnap-like replacement that is user-configurable.

![Example screenshot](https://cdn-images-1.medium.com/max/800/1*SORR1QlyBeh0bcuMBYQyJQ.png)


FAQ
===

### Why does this exist?
AeroSnap doesn't work for all use cases. In particular it fails for monitors in portrait mode. AeroSnap produces two ultra-skinny 540x1920 windows side-by-side; the preferred layout is two 1920x540 windows stacked vertically.

fts_winsnap is user-configurable per-monitor so windows can snap to arbitrary rectangles. Portrait mode and ultrawide monitors rejoice!

### What license?
This is software is dual-licensed under either the MIT License or Unlicense. Choose whichever you prefer.

### What did you learn?
More than I expected. I wrote a [blog post](https://blog.forrestthewoods.com/building-a-better-aero-snap-757f68a1305f) detailing so hurdles and lessons learned.

### What platforms are supported?
Just Windows. I wrote the tool in C# and used the native Win32 API.


Binaries
===

Here are some pre-compiled binaries.
 
* [Windows](https://github.com/forrestthewoods/fts_winsnap/releases/download/0.1/winsnap.zip)
