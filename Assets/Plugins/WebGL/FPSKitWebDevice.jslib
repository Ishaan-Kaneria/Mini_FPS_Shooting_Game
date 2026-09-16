// Asks the browser what kind of pointer it actually has.
//
// Unity's own answer on WebGL is Module.SystemInfo.mobile, which is a user-agent test:
// /Mobile|Android|iP(ad|hone)/. An iPad has reported itself as a Macintosh since
// iPadOS 13, so that test calls it a desktop, the on-screen controls stay hidden, and
// the game arrives on the one device that has no other way to play it.
//
// The pointer media queries are the test that survives that. A phone or tablet has a
// coarse pointer and no fine one; a touchscreen laptop has both, and must keep being
// treated as a desktop or it loses mouse look.
mergeInto(LibraryManager.library, {

  FPSKitPointerIsCoarseOnly: function () {
    try {
      if (!window.matchMedia) return 0;
      var coarse = window.matchMedia("(any-pointer: coarse)").matches;
      var fine   = window.matchMedia("(any-pointer: fine)").matches;
      return (coarse && !fine) ? 1 : 0;
    } catch (e) {
      return 0;
    }
  }

});
