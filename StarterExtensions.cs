namespace Heft
{
    internal static class StarterExtensions
    {
        public const string Template = """
heft.command('hello', 'Say hello', () => heft.notify('hello', 'your extension works'));
heft.bar('hi', '\uE76E', () => heft.notify('clicked', 'you clicked your bar item'));
""";

        public static readonly (string id, string manifest, string code)[] All =
        {
            ("pomodoro", """
{
  "id": "pomodoro",
  "name": "Pomodoro",
  "version": "1.0",
  "author": "heft",
  "description": "25 minutes on, 5 off, right in the top bar. click it to start or stop",
  "permissions": ["bar", "commands", "notify"]
}
""", """
const work = 25 * 60;
const rest = 5 * 60;
let mode = 'idle';
let left = 0;

function fmt(s) {
  const m = Math.floor(s / 60);
  const r = s % 60;
  return m + ':' + String(r).padStart(2, '0');
}

function draw() {
  if (mode === 'idle') heft.bar('', '\uE916', toggle);
  else heft.bar((mode === 'work' ? 'focus ' : 'break ') + fmt(left), mode === 'work' ? '\uE916' : '\uE708', toggle);
}

function start(m) {
  mode = m;
  left = m === 'work' ? work : rest;
  draw();
}

function stop() {
  mode = 'idle';
  left = 0;
  draw();
}

function toggle() {
  if (mode === 'idle') start('work');
  else stop();
}

heft.every(1000, () => {
  if (mode === 'idle') return;
  left--;
  if (left > 0) {
    draw();
    return;
  }
  if (mode === 'work') {
    heft.notify('break time', 'nice work. take 5');
    start('rest');
  } else {
    heft.notify('back at it', 'break is over');
    start('work');
  }
});

heft.command('start', 'Pomodoro: start', () => start('work'));
heft.command('stop', 'Pomodoro: stop', stop);
draw();
"""),
            ("github", """
{
  "id": "github",
  "name": "GitHub",
  "version": "1.0",
  "author": "heft",
  "description": "your GitHub notification count in the bar. put a personal access token in token.txt in this folder",
  "permissions": ["bar", "commands", "notify", "net", "run"]
}
""", """
const token = heft.file('token.txt').trim();

function open() {
  heft.open('https://github.com/notifications');
}

function check() {
  if (!token) {
    heft.bar('', '\uE8BD', () => heft.notify('GitHub', "put a token in token.txt in this extension's folder"));
    return;
  }
  const headers = JSON.stringify({ Authorization: 'Bearer ' + token, Accept: 'application/vnd.github+json' });
  heft.request('https://api.github.com/notifications', headers, (text, status) => {
    if (status !== 200) {
      heft.bar('!', '\uE8BD', open);
      return;
    }
    const list = JSON.parse(text);
    heft.bar(list.length > 0 ? String(list.length) : '', '\uE8BD', open);
    if (!list.length) return;
    const newest = Date.parse(list[0].updated_at) || 0;
    const seen = Number(heft.load('seen') || 0);
    if (seen > 0 && newest > seen) heft.notify('GitHub', list[0].subject.title);
    if (newest > seen) heft.save('seen', String(newest));
  });
}

heft.every(120000, check);
heft.command('open', 'GitHub notifications', open);
check();
"""),
            ("listening", """
{
  "id": "listening",
  "name": "Listening to",
  "version": "1.0",
  "author": "heft",
  "description": "copies a little 'listening to ...' status for whatever's playing",
  "permissions": ["media", "commands", "clipboard", "notify"]
}
""", """
heft.command('copy', "Copy what I'm listening to", () => {
  const m = heft.media();
  if (!m.any || !m.title) {
    heft.notify("nothing's playing", '');
    return;
  }
  const text = 'listening to ' + m.title + (m.artist ? ' by ' + m.artist : '');
  heft.copy(text);
  heft.notify('copied', text);
});
"""),
            ("world-clock", """
{
  "id": "world-clock",
  "name": "World clock",
  "version": "1.0",
  "author": "heft",
  "description": "other time zones in the bar (click to flip) and on Start. edit cities.json to pick your own",
  "permissions": ["bar", "widgets"]
}
""", """
const fallback = [['Tokyo', 'Asia/Tokyo'], ['London', 'Europe/London'], ['LA', 'America/Los_Angeles']];
let cities = fallback;
try {
  const raw = heft.file('cities.json');
  if (raw) cities = JSON.parse(raw);
} catch (e) {
  cities = fallback;
}
let i = 0;

function draw() {
  const pick = cities[i % cities.length];
  heft.bar(pick[0] + ' ' + heft.time(pick[1], 'h:mm tt').toLowerCase(), '\uE12B', () => {
    i++;
    draw();
  });
  heft.widget('World clock', cities.map(c => c[0] + '   ' + heft.time(c[1], 'h:mm tt').toLowerCase()).join('\n'));
}

heft.every(15000, draw);
draw();
"""),
            ("lyrics", """
{
  "id": "lyrics",
  "name": "Lyrics",
  "version": "1.0",
  "author": "heft",
  "description": "the current lyric line in the bar while music plays. lyrics come from lrclib.net",
  "permissions": ["media", "net", "bar"]
}
""", """
let lines = [];
let key = '';

function parse(lrc) {
  const out = [];
  for (const raw of lrc.split('\n')) {
    const m = raw.match(/^\[(\d+):(\d+(?:\.\d+)?)\](.*)$/);
    if (m) out.push({ t: Number(m[1]) * 60 + Number(m[2]), text: m[3].trim() });
  }
  return out;
}

function load(m) {
  const k = m.artist + '|' + m.title;
  if (k === key) return;
  key = k;
  lines = [];
  if (!m.title) return;
  const url = 'https://lrclib.net/api/get?artist_name=' + encodeURIComponent(m.artist) + '&track_name=' + encodeURIComponent(m.title);
  heft.fetch(url, (text, status) => {
    if (status !== 200 || key !== k) return;
    const data = JSON.parse(text);
    if (data.syncedLyrics) lines = parse(data.syncedLyrics);
  });
}

heft.every(1000, () => {
  const m = heft.media();
  if (!m.any || !m.playing) {
    heft.bar('');
    return;
  }
  load(m);
  if (!lines.length) {
    heft.bar('');
    return;
  }
  let cur = '';
  for (const l of lines) {
    if (l.t <= m.position + 0.3) cur = l.text;
    else break;
  }
  heft.bar(cur || '...', '\uE8D6');
});
""")
        };
    }
}
