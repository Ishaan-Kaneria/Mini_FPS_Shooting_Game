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
