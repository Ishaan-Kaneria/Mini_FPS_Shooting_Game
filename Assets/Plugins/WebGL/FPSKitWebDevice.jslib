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
  },

  // The density of the framebuffer Unity is actually drawing into, in pixels per inch.
  //
  // <b>Screen.dpi cannot answer this in a browser and the ways it is wrong pull in
  // opposite directions.</b> Unity's WebGL runtime returns 96 multiplied by the pixel
  // ratio the page configured, so with the phone path rendering at ratio 1 it reports
  // 96 -- below the handheld floor, so the touch layer substituted "a typical phone",
  // 400, while DeviceProfile took 96 at face value and measured a 152mm handset as a
  // 242mm tablet. One reading, two wrong answers: controls tuned for a density nothing
  // has, and a phone handed the desktop's menus.
  //
  // So the page is asked instead, and it answers with the one thing a browser really
  // does know: how big a CSS pixel is. A CSS pixel is not a fixed length, but it is a
  // fixed *intent* -- mobile browsers pick the ideal viewport so that text at 16px is
  // readable in the hand, which lands every phone and tablet near 150 CSS dpi, while a
  // desktop sits at the spec's 96. Measured against real hardware that is within a few
  // millimetres: a 914 CSS pixel landscape phone comes out 155mm against a true 152mm.
  //
  // Multiplying by the ratio the page renders at converts that into the framebuffer's
  // own density, which is the number every millimetre in this game is converted with.
  // It therefore stays correct whatever sharpness the page chooses: raising the ratio
  // gives Unity more pixels per millimetre and more pixels per screen, and the physical
  // size of a button is unchanged.
  //
  // Returned as an integer per-mille so the value survives the float marshalling cleanly.
  FPSKitFramebufferDpi: function () {
    try {
      var coarse = false;

      if (window.matchMedia) {
        coarse = window.matchMedia("(any-pointer: coarse)").matches &&
                 !window.matchMedia("(any-pointer: fine)").matches;
      }

      // What the page told createUnityInstance to render at, which is what the runtime
      // sizes the canvas by. window.devicePixelRatio is the fallback rather than the
      // answer: it describes the glass, not the backbuffer, and the two differ by
      // design on a phone.
      var ratio = (typeof Module !== "undefined" && Module.devicePixelRatio) ||
                  window.fpskitRenderRatio ||
                  window.devicePixelRatio || 1;

      if (!isFinite(ratio) || ratio <= 0) ratio = 1;

      return Math.round((coarse ? 150 : 96) * ratio * 1000);
    } catch (e) {
      return 96000;
    }
  },

  // Leaves the game, for the dashboard's Exit button.
  //
  // There is no process to end in a browser, so Application.Quit() only tears the
  // player down and leaves a dead canvas sitting on the page -- which is what made
  // quitting feel broken here. window.close() is the honest first try, but browsers
  // refuse it for any tab a script did not open itself, and they refuse silently.
  //
  // So the close is attempted and then checked a moment later: if the document is
  // still here, the tab was never ours to close, and the page says goodbye properly
  // instead. The hand-off is the point -- whatever happens, the player ends up looking
  // at something deliberate rather than at a frozen frame of the arena.
  FPSKitExit: function () {
    try {
      var farewell = function () {
        if (window.closed) return;

        var page = document.getElementById("fpskit-farewell");
        if (page) return;

        page = document.createElement("div");
        page.id = "fpskit-farewell";
        page.setAttribute("role", "status");
        page.style.cssText = [
          "position:fixed", "inset:0", "z-index:2147483647",
          "display:flex", "flex-direction:column",
          "align-items:center", "justify-content:center", "gap:1.25rem",
          "background:#0b0d10", "color:#e8e6e3",
          "font-family:ui-monospace,SFMono-Regular,Menlo,Consolas,monospace",
          "text-align:center", "padding:2rem"
        ].join(";");

        var title = document.createElement("div");
        title.textContent = "THANKS FOR PLAYING";
        title.style.cssText = "font-size:clamp(1.5rem,5vw,2.5rem);letter-spacing:.35em;font-weight:700";

        var note = document.createElement("div");
        note.textContent = "You can close this tab, or reload to play again.";
        note.style.cssText = "font-size:clamp(.85rem,2.5vw,1rem);opacity:.65;letter-spacing:.08em";

        var again = document.createElement("button");
        again.textContent = "PLAY AGAIN";
        again.style.cssText = [
          "margin-top:.5rem", "padding:.75rem 1.75rem", "cursor:pointer",
          "background:#e8e6e3", "color:#0b0d10", "border:0",
          "font:inherit", "font-weight:700", "letter-spacing:.2em"
        ].join(";");
        again.onclick = function () { window.location.reload(); };

        page.appendChild(title);
        page.appendChild(note);
        page.appendChild(again);
        document.body.appendChild(page);
      };

      try { window.close(); } catch (e) { /* refused; the check below handles it */ }

      // Closing, when it is allowed, happens before this fires.
      window.setTimeout(farewell, 150);
    } catch (e) {
      // Never let leaving the game throw back into the player.
    }
  }

});
