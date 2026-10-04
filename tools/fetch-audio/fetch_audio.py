#!/usr/bin/env python3
"""Downloads CC0 sounds from Freesound for the sound bank (data/base/audio.json) and records the credits.

Needs a Freesound API key in the FREESOUND_API_KEY environment variable (see docs/AUDIO.md). Without a key it
prints how to get one and exits successfully: the game keeps its synthesised sounds.

    FREESOUND_API_KEY=... python3 tools/fetch-audio/fetch_audio.py [--only engine.] [--force]

For every sound entry it searches Freesound with the entry's `query`, restricted to license "Creative Commons 0"
and the entry's duration range, takes the best rated result and saves its high quality OGG preview as
Unity/Assets/Garage/Resources/Audio/<id>.ogg. Credits go to docs/ASSET_CREDITS.md (section FREESOUND).
Only the Python standard library is used.
"""
import json
import os
import sys
import urllib.parse
import urllib.request

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BANK = os.path.join(ROOT, "data", "base", "audio.json")
OUT = os.path.join(ROOT, "Unity", "Assets", "Garage", "Resources", "Audio")
CREDITS = os.path.join(ROOT, "docs", "ASSET_CREDITS.md")
API = "https://freesound.org/apiv2"
MARK_START, MARK_END = "<!-- FREESOUND -->", "<!-- /FREESOUND -->"


def get_json(url):
    req = urllib.request.Request(url, headers={"User-Agent": "GarageSim fetch-audio"})
    with urllib.request.urlopen(req, timeout=30) as r:
        return json.load(r)


def search(key, query, duration):
    lo, hi = duration if duration else (0.1, 60)
    params = {
        "query": query,
        "filter": f'license:"Creative Commons 0" duration:[{lo} TO {hi}]',
        "sort": "rating_desc",
        "fields": "id,name,username,license,previews,url,duration",
        "page_size": "5",
        "token": key,
    }
    return get_json(f"{API}/search/text/?{urllib.parse.urlencode(params)}").get("results", [])


def write_credits(rows):
    text = open(CREDITS, encoding="utf-8").read() if os.path.exists(CREDITS) else "# Créditos de assets\n"
    block = [MARK_START, "", "| Sonido del juego | Grabación | Autor | Licencia | Origen |", "|---|---|---|---|---|"]
    block += [f"| `{r['id']}` | {r['name']} | {r['user']} | CC0 1.0 | {r['url']} |" for r in sorted(rows, key=lambda r: r["id"])]
    block += ["", MARK_END]
    if MARK_START in text and MARK_END in text:
        head, rest = text.split(MARK_START, 1)
        tail = rest.split(MARK_END, 1)[1]
        text = head + "\n".join(block) + tail
    else:
        text = text.rstrip() + "\n\n## Sonidos de Freesound (CC0)\n\n" + "\n".join(block) + "\n"
    open(CREDITS, "w", encoding="utf-8").write(text)


def existing_credits():
    rows = {}
    if not os.path.exists(CREDITS):
        return rows
    text = open(CREDITS, encoding="utf-8").read()
    if MARK_START not in text:
        return rows
    for line in text.split(MARK_START, 1)[1].split(MARK_END, 1)[0].splitlines():
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if len(cells) == 5 and cells[0].startswith("`"):
            rows[cells[0].strip("`")] = {"id": cells[0].strip("`"), "name": cells[1], "user": cells[2], "url": cells[4]}
    return rows


def main():
    args = sys.argv[1:]
    only = args[args.index("--only") + 1] if "--only" in args else ""
    force = "--force" in args
    key = os.environ.get("FREESOUND_API_KEY", "").strip()
    if not key:
        print("FREESOUND_API_KEY no está definida: se mantiene el audio sintético del juego.")
        print("Cómo obtener una clave gratuita: docs/AUDIO.md (https://freesound.org/apiv2/apply/).")
        return 0
    bank = json.load(open(BANK, encoding="utf-8"))["sounds"]
    os.makedirs(OUT, exist_ok=True)
    credits = existing_credits()
    ok = failed = 0
    for s in bank:
        sid = s["id"]
        if only and not sid.startswith(only):
            continue
        target = os.path.join(OUT, sid + ".ogg")
        if os.path.exists(target) and not force:
            continue
        try:
            results = search(key, s.get("query", sid), s.get("duration"))
        except Exception as e:  # network or API error: keep going, report at the end
            print(f"  {sid}: error de búsqueda ({e})")
            failed += 1
            continue
        pick = next((r for r in results if r.get("license", "").endswith("/zero/1.0/") and "preview-hq-ogg" in r.get("previews", {})), None)
        if pick is None:
            print(f"  {sid}: sin resultados CC0 para «{s.get('query')}»")
            failed += 1
            continue
        req = urllib.request.Request(pick["previews"]["preview-hq-ogg"], headers={"User-Agent": "GarageSim fetch-audio"})
        with urllib.request.urlopen(req, timeout=60) as r, open(target, "wb") as f:
            f.write(r.read())
        credits[sid] = {"id": sid, "name": pick["name"].replace("|", "/"), "user": pick["username"], "url": pick["url"]}
        print(f"  {sid}: «{pick['name']}» de {pick['username']} ({pick['duration']:.1f} s)")
        ok += 1
    write_credits(list(credits.values()))
    print(f"Descargados {ok}, fallidos {failed}. Créditos en docs/ASSET_CREDITS.md. Reabre Unity para importarlos.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
