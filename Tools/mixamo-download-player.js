// Mixamo batch download: the player's full body (kicks, punches, rifle butt) and last time's missing clips.
//
// How to use:
//   1. Log in at https://www.mixamo.com in Chrome or Firefox and leave the page open on the
//      Animations tab (any character is fine; the default Y Bot is what we want).
//   2. Press F12, open the Console tab, paste this whole file, press Enter.
//      (Chrome may ask you to type "allow pasting" first.)
//   3. When the browser asks "download multiple files?", choose Allow.
//   4. Wait for "ALL DONE". Files land in your Downloads folder as  <Group>__<Clip name>.fbx,
//      plus mixamo-manifest.json. Tell Claude when it is done; it sorts them into
//      Assets/Animations/Mixamo/<Group>/.
//
// Your login never leaves the browser: the script uses the page's own session token to call
// the same endpoints the Mixamo site calls when you press Download. Those endpoints are not
// documented, so if something fails the console says what, and the log is enough to fix it.
//
// The two big packs are NOT in here (packs export differently). Download them by hand with
// the same settings: search "Pro Rifle Pack" and "Rifle 8-Way Locomotion Pack", click the
// pack, DOWNLOAD, Format "FBX for Unity", Skin "Without Skin", 30 fps, Keyframe Reduction none.

(async () => {
  // ---- settings (the same as the Download dialog) ----
  const FORMAT = 'fbx7_unity';      // "FBX for Unity (.fbx)"; falls back to 'fbx7' (FBX Binary)
  const FPS = '30';
  const SKIN = 'false';             // Without Skin
  const REDUCE_KEYFRAMES = '0';     // none
  const IN_PLACE = false;           // root motion kept
  const CHARACTER_ID = '';          // leave empty: the script asks Mixamo for your current one

  // ---- what to fetch: [group, search text, how many results to take, preferred exact names] ----
  // The preferred names are taken first when a search returns them; otherwise the top results.
  const WANT = [
    // The player's body (first person): kicks, punches, rifle-butt strikes.
    ['Player', 'front kick', 2, ['Front Kick']],
    ['Player', 'mma kick', 1, ['Mma Kick']],
    ['Player', 'kicking', 2, []],
    ['Player', 'jab cross', 1, ['Jab Cross']],
    ['Player', 'punching', 2, ['Punching']],
    ['Player', 'lead jab', 1, ['Lead Jab']],
    ['Player', 'cross punch', 1, ['Cross Punch']],
    ['Player', 'rifle butt', 2, []],
    ['Player', 'butt stroke', 1, []],
    ['Player', 'rifle melee', 2, []],
    ['Player', 'firing rifle', 1, ['Firing Rifle']],
    // Fists up between punches, the rifle on the back.
    ['Player', 'boxing idle', 1, ['Boxing Idle']],
    ['Player', 'fight idle', 1, ['Fight Idle']],
    // Left over from last time (Firefox dropped them).
    ['Actions', 'reload', 4, ['Reload', 'Rifle Reload', 'Crouch Reload']],
    ['Injured', 'injured stumble idle', 1, ['Injured Stumble Idle']],
  ];

  // ---- plumbing ----
  const token = localStorage.access_token;
  if (!token) { console.error('Not logged in: no access_token on this page. Log in at mixamo.com and run again.'); return; }
  const headers = {
    Accept: 'application/json',
    'Content-Type': 'application/json',
    Authorization: `Bearer ${token}`,
    'X-Api-Key': 'mixamo2',
  };
  const api = 'https://www.mixamo.com/api/v1';
  const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
  const get = async (url) => {
    const res = await fetch(url, { headers });
    if (!res.ok) throw new Error(`${res.status} on GET ${url}`);
    return res.json();
  };

  let characterId = CHARACTER_ID;
  if (!characterId) {
    const primary = await get(`${api}/characters/primary`);
    characterId = primary.primary_character_id || primary.character_id || primary.id;
    if (!characterId) {
      console.error('Could not find your character id. Reply to Claude with this:', primary);
      return;
    }
  }
  console.log('Character', characterId);

  const search = async (text) => {
    const q = encodeURIComponent(text);
    const json = await get(`${api}/products?page=1&limit=48&order=&type=Motion&query=${q}`);
    return (json.results || []).filter((r) => r.type === 'Motion');
  };

  const monitor = async () => {
    for (let i = 0; i < 180; i++) {
      const res = await fetch(`${api}/characters/${characterId}/monitor`, { headers });
      if (res.status === 200 || res.status === 202) {
        const msg = await res.json();
        if (msg.status === 'completed') return msg.job_result;
        if (msg.status === 'failed') throw new Error('export failed: ' + JSON.stringify(msg));
      } else if (res.status !== 404) {
        throw new Error(`${res.status} while waiting for the export`);
      }
      await sleep(1000);
    }
    throw new Error('export timed out');
  };

  const exportClip = async (product, format) => {
    const details = await get(`${api}/products/${product.id}?similar=0&character_id=${characterId}`);
    const hash = Object.assign({}, details.details.gms_hash);
    // The default slider values, as the site sends them.
    hash.params = (hash.params || []).map((p) => p[1]).join(',');
    hash.inplace = IN_PLACE;
    hash.mirror = false;
    const body = {
      character_id: characterId,
      gms_hash: [hash],
      preferences: { format, skin: SKIN, fps: FPS, reducekf: REDUCE_KEYFRAMES },
      product_name: product.name,
      type: 'Motion',
    };
    const res = await fetch(`${api}/animations/export`, { method: 'POST', headers, body: JSON.stringify(body) });
    if (!res.ok) throw new Error(`${res.status} on export (${format})`);
    return monitor();
  };

  const save = async (url, filename) => {
    try {
      const res = await fetch(url);
      if (!res.ok) throw new Error(String(res.status));
      const blob = await res.blob();
      const a = document.createElement('a');
      a.href = URL.createObjectURL(blob);
      a.download = filename;
      document.body.appendChild(a);
      a.click();
      a.remove();
      setTimeout(() => URL.revokeObjectURL(a.href), 10000);
      return filename;
    } catch (e) {
      // The file host refused a script fetch: let the browser download it under its own name.
      const a = document.createElement('a');
      a.href = url;
      document.body.appendChild(a);
      a.click();
      a.remove();
      return '(browser name)';
    }
  };

  const clean = (s) => s.replace(/[^A-Za-z0-9 _-]/g, '').trim();
  const done = new Set();
  const manifest = [];
  let format = FORMAT;

  for (const [group, text, take, prefer] of WANT) {
    if (take <= 0) continue;
    let results;
    try { results = await search(text); } catch (e) { console.warn('search failed', text, e.message); continue; }
    console.log(`"${text}" ->`, results.slice(0, 10).map((r) => r.name).join(' | '));

    const lower = prefer.map((n) => n.toLowerCase());
    const preferred = results.filter((r) => lower.includes(r.name.toLowerCase()));
    const rest = results.filter((r) => !preferred.includes(r));
    const picks = preferred.concat(rest).filter((r) => !done.has(r.id)).slice(0, take);

    for (const product of picks) {
      done.add(product.id);
      const name = `${group}__${clean(product.name)}.fbx`;
      try {
        let url;
        try { url = await exportClip(product, format); }
        catch (e) {
          if (format !== 'fbx7') { console.warn(`format ${format} refused, using fbx7`); format = 'fbx7'; url = await exportClip(product, format); }
          else throw e;
        }
        const saved = await save(url, name);
        manifest.push({ group, name: product.name, id: product.id, description: product.description || '', file: saved, query: text });
        console.log(`OK  ${group} / ${product.name}`);
      } catch (e) {
        manifest.push({ group, name: product.name, id: product.id, error: e.message, query: text });
        console.warn(`FAIL ${group} / ${product.name}: ${e.message}`);
      }
      await sleep(1500);
    }
  }

  const blob = new Blob([JSON.stringify({ format, fps: FPS, characterId, clips: manifest }, null, 2)], { type: 'application/json' });
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = 'mixamo-manifest.json';
  a.click();
  console.log(`ALL DONE: ${manifest.filter((m) => !m.error).length} downloaded, ${manifest.filter((m) => m.error).length} failed.`);
})();
