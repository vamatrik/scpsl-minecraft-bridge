#!/usr/bin/env python3
"""Генерирует рецепты предметов SCP:SL для мода (datapack), таблицу для лаунчера и README.
Запуск: python3 gen_recipes.py <fabric_resources_dir> <launcher_dir> <readme_fragment.md> [vanilla_recipe_dir]"""
import json, os, sys, math, glob

RES, LAUNCHER, README = sys.argv[1], sys.argv[2], sys.argv[3]
VANILLA = sys.argv[4] if len(sys.argv) > 4 else None

# Обозначения в сетках: символ -> (id, подпись, цвет для лаунчера)
LEG = {
    '#': ('minecraft:iron_ingot',  'Железный слиток', '#D8D8D8'),
    'B': ('minecraft:iron_block',  'Железный блок',   '#A9A9A9'),
    'D': ('minecraft:diamond',     'Алмаз',           '#4EE2EC'),
    'N': ('minecraft:netherite_ingot', 'Незеритовый слиток', '#4A3F45'),
    'G': ('minecraft:gunpowder',   'Порох',           '#6E6E6E'),
    'S': ('minecraft:stick',       'Палка',           '#8B5A2B'),
    'R': ('minecraft:redstone',    'Редстоун',        '#C81E1E'),
    'T': ('minecraft:torch',       'Факел',           '#FFB43C'),
    'X': ('minecraft:tnt',         'Динамит',         '#D2382B'),
    'L': ('minecraft:glowstone_dust', 'Светокаменная пыль', '#F2D46B'),
    'U': ('minecraft:sugar',       'Сахар',           '#F5F5F5'),
    'O': ('minecraft:glass_bottle','Пузырёк',         '#BFE3F0'),
    'C': ('minecraft:cocoa_beans', 'Какао-бобы',      '#7B4A2A'),
    'E': ('minecraft:spider_eye',  'Паучий глаз',     '#A63B4A'),
    'M': ('minecraft:slime_ball',  'Слизь',           '#7CC46A'),
    'H': ('minecraft:leather_helmet', 'Кожаный шлем', '#8E5B3A'),
    'P': ('minecraft:ender_pearl', 'Жемчуг Края',     '#2F8F7F'),
    'F': ('minecraft:flower_pot',  'Цветочный горшок','#9C5A3C'),
    'I': ('minecraft:packed_ice',  'Плотный лёд',     '#9CC3F0'),
    'Y': ('minecraft:blue_ice',    'Синий лёд',       '#6FA8F0'),
    'W': ('minecraft:bowl',        'Миска',           '#8B6A43'),
    'K': ('minecraft:note_block',  'Нотный блок',     '#6B4A33'),
    'V': ('minecraft:ender_eye',   'Око Края',        '#3E9E6A'),
    'Q': ('minecraft:glass_pane',  'Стеклянная панель','#CFE8F0'),
    'A': ('minecraft:glass',       'Стекло',          '#DDF0F5'),
    'Z': ('minecraft:copper_ingot','Медный слиток',   '#C87850'),
}

# (ключ ItemType SL, имя, иконка, группа, форма|список для бесформенных, кол-во)
SHAPED = [
    # ── оружие: только железные слитки ──
    ('GunCOM15',   'COM-15',            'crossbow', 'Оружие', ['##', '# '], 1),
    ('GunCOM18',   'COM-18',            'crossbow', 'Оружие', ['###', '#  '], 1),
    ('GunCom45',   'COM-45',            'crossbow', 'Оружие', ['###', '## '], 1),
    ('GunRevolver','.44 Револьвер',     'crossbow', 'Оружие', ['###', ' # '], 1),
    ('GunFSP9',    'FSP-9',             'crossbow', 'Оружие', ['###', '#  ', '#  '], 1),
    ('GunCrossvec','Crossvec',          'crossbow', 'Оружие', ['###', '###', '#  '], 1),
    ('GunE11SR',   'MTF-E11-SR',        'crossbow', 'Оружие', ['###', '###', ' # '], 1),
    ('GunAK',      'AK',                'crossbow', 'Оружие', ['###', '## ', '#  '], 1),
    ('GunA7',      'A7',                'crossbow', 'Оружие', ['###', ' ##', ' # '], 1),
    ('GunShotgun', 'Дробовик',          'crossbow', 'Оружие', ['###', '###', '# #'], 1),
    ('GunLogicer', 'Logicer',           'crossbow', 'Оружие', ['###', '###', '## '], 1),
    ('GunFRMG0',   'FR-MG-0',           'crossbow', 'Оружие', ['###', '# #', '## '], 1),
    # ── особое оружие ──
    ('GunSCP127',  'SCP-127',           'crossbow', 'Особое оружие', ['##D', '#  '], 1),
    ('ParticleDisruptor', 'Молекулярный разрушитель', 'trident', 'Особое оружие', ['##B', '#  '], 1),
    ('MicroHID',   'Micro H.I.D.',      'lightning_rod', 'Особое оружие', ['###', 'DDD', '###'], 1),
    ('Jailbird',   'Дубинка (Jailbird)','mace',     'Особое оружие', ['D', '#', '#'], 1),
    ('SCP1509',    'Мачете (SCP-1509)', 'iron_sword','Особое оружие', ['  #', ' # ', 'S  '], 1),
    # ── патроны (по 80 за крафт) ──
    ('Ammo9x19',   'Патроны 9x19 (80)',     'iron_nugget', 'Патроны', ['G', '#'], 1),
    ('Ammo556x45', 'Патроны 5.56x45 (80)',  'iron_nugget', 'Патроны', ['G', '#', '#'], 1),
    ('Ammo762x39', 'Патроны 7.62x39 (80)',  'iron_nugget', 'Патроны', ['G', 'G', '#'], 1),
    ('Ammo12gauge','Патроны 12 калибра (80)','iron_nugget','Патроны', ['GG', '##'], 1),
    ('Ammo44cal',  'Патроны .44 (80)',      'iron_nugget', 'Патроны', ['#G#'], 1),
    # ── броня (заменяет ванильные кирасы — в мосте броня всё равно берётся из SL) ──
    ('ArmorLight', 'Лёгкая броня',      'iron_chestplate',      'Броня', ['# #', '###', '###'], 1),
    ('ArmorCombat','Боевая броня',      'diamond_chestplate',   'Броня', ['D D', 'DDD', 'DDD'], 1),
    ('ArmorHeavy', 'Тяжёлая броня',     'netherite_chestplate', 'Броня', ['DND', 'DDD', 'DDD'], 1),
    # ── прочее ──
    ('Radio',      'Рация',             'compass',  'Снаряжение', ['#', 'R', '#'], 1),
    ('Flashlight', 'Фонарик',           'lantern',  'Снаряжение', ['T', '#', '#'], 1),
    ('SCP1344',    'SCP-1344 (очки)',   'spyglass', 'SCP', ['QVQ'], 1),
]
SHAPELESS = [
    ('GrenadeHE',   'Осколочная граната', 'fire_charge', 'Снаряжение', ['#', 'X'], 1),
    ('GrenadeFlash','Светошумовая граната','glowstone_dust','Снаряжение', ['#', 'L', 'G'], 1),
    ('Painkillers', 'Обезболивающее',     'bone_meal',  'Медицина', ['U', 'U', 'O'], 1),
    ('Adrenaline',  'Адреналин',          'experience_bottle', 'Медицина', ['minecraft:golden_apple'], 1),
    ('SCP500',      'SCP-500 (панацея)',  'redstone',   'Медицина', ['minecraft:enchanted_golden_apple'], 1),
    ('SCP207',      'SCP-207',            'honey_bottle','SCP', ['U', 'U', 'C', 'O'], 1),
    ('AntiSCP207',  'Анти-SCP-207',       'potion',     'SCP', ['U', 'U', 'E', 'O'], 1),
    ('SCP018',      'SCP-018 (мяч)',      'slime_ball', 'SCP', ['M', 'M', 'R'], 1),
    ('SCP268',      'SCP-268 (кепка)',    'leather_helmet', 'SCP', ['H', 'P'], 1),
    ('SCP244a',     'SCP-244-A (ваза)',   'flower_pot', 'SCP', ['F', 'I'], 1),
    ('SCP244b',     'SCP-244-B (ваза)',   'flower_pot', 'SCP', ['F', 'Y'], 1),
    ('SCP1853',     'SCP-1853',           'dragon_breath', 'SCP', ['O', 'L', 'U'], 1),
    ('SCP2176',     'SCP-2176 (лампа)',   'end_crystal','SCP', ['A', 'R', 'L'], 1),
    ('SCP330',      'SCP-330 (конфеты)',  'bowl',       'SCP', ['W', 'U', 'U', 'U'], 1),
    ('SCP1576',     'SCP-1576 (граммофон)','jukebox',   'SCP', ['K', 'P'], 1),
]

# еда -> голод (сколько единиц восстанавливает); 10 единиц = 1 аптечка
FOOD = {
 'apple':4,'baked_potato':5,'beef':3,'beetroot':1,'beetroot_soup':6,'bread':5,'carrot':3,'chicken':2,
 'chorus_fruit':4,'cod':2,'cooked_beef':8,'cooked_chicken':6,'cooked_cod':5,'cooked_mutton':6,
 'cooked_porkchop':8,'cooked_rabbit':5,'cooked_salmon':6,'cookie':2,'dried_kelp':1,'glow_berries':2,
 'golden_carrot':6,'honey_bottle':6,'melon_slice':2,'mushroom_stew':6,'mutton':2,'poisonous_potato':2,
 'porkchop':3,'potato':1,'pufferfish':1,'pumpkin_pie':8,'rabbit':3,'rabbit_stew':10,'rotten_flesh':4,
 'salmon':2,'spider_eye':2,'suspicious_stew':6,'sweet_berries':2,'tropical_fish':1,
}
FOOD_RU = {
 'apple':'Яблоко','baked_potato':'Печёный картофель','beef':'Сырая говядина','beetroot':'Свёкла','beetroot_soup':'Свекольный суп',
 'bread':'Хлеб','carrot':'Морковь','chicken':'Сырая курица','chorus_fruit':'Плод хоруса','cod':'Сырая треска',
 'cooked_beef':'Стейк','cooked_chicken':'Жареная курица','cooked_cod':'Жареная треска','cooked_mutton':'Жареная баранина',
 'cooked_porkchop':'Жареная свинина','cooked_rabbit':'Жареная крольчатина','cooked_salmon':'Жареный лосось','cookie':'Печенье',
 'dried_kelp':'Сушёная ламинария','glow_berries':'Светящиеся ягоды','golden_carrot':'Золотая морковь','honey_bottle':'Бутылочка мёда',
 'melon_slice':'Ломтик арбуза','mushroom_stew':'Грибной суп','mutton':'Сырая баранина','poisonous_potato':'Ядовитый картофель',
 'porkchop':'Сырая свинина','potato':'Картофель','pufferfish':'Иглобрюх','pumpkin_pie':'Тыквенный пирог','rabbit':'Сырая крольчатина',
 'rabbit_stew':'Тушёный кролик','rotten_flesh':'Гнилая плоть','salmon':'Сырой лосось','spider_eye':'Паучий глаз',
 'suspicious_stew':'Подозрительное рагу','sweet_berries':'Сладкие ягоды','tropical_fish':'Тропическая рыба',
}

def result(key, name, icon, count):
    return {"id": "minecraft:paper", "count": count, "components": {
        "minecraft:custom_data": {"slItem": key},
        "minecraft:item_name": name,
        "minecraft:item_model": "minecraft:" + icon,
        "minecraft:enchantment_glint_override": True,
        "minecraft:rarity": "rare"}}

def ing(c): return c if ':' in c else LEG[c][0]

out = {}   # путь -> json
table = [] # для лаунчера/README
def put(path, obj): out[path] = obj

for key, name, icon, grp, pat, cnt in SHAPED:
    used = sorted({c for r in pat for c in r if c != ' '})
    obj = {"type": "minecraft:crafting_shaped", "category": "equipment", "pattern": pat,
           "key": {c: LEG[c][0] for c in used}, "result": result(key, name, icon, cnt)}
    rid = key.lower()
    put(f"data/slbridge/recipe/{rid}.json", obj)
    table.append(dict(key=key, name=name, group=grp, shaped=True, grid=pat, legend=used, count=cnt))
for key, name, icon, grp, ings, cnt in SHAPELESS:
    obj = {"type": "minecraft:crafting_shapeless", "category": "misc",
           "ingredients": [ing(c) for c in ings], "result": result(key, name, icon, cnt)}
    put(f"data/slbridge/recipe/{key.lower()}.json", obj)
    table.append(dict(key=key, name=name, group=grp, shaped=False, items=ings, count=cnt))

# Аптечки из еды
medkits = []
for food, n in sorted(FOOD.items(), key=lambda x: (-x[1], x[0])):
    variants = set()
    k = min(9, math.ceil(10 / n)); variants.add(k)
    for kk in range(1, 10):           # «без остатка», если влезает в сетку
        if (kk * n) % 10 == 0: variants.add(kk); break
    for k in sorted(variants):
        outn = max(1, (k * n) // 10)
        obj = {"type": "minecraft:crafting_shapeless", "category": "misc", "group": "slbridge_medkit",
               "ingredients": ["minecraft:" + food] * k, "result": result("Medkit", "Аптечка", "red_dye", outn)}
        put(f"data/slbridge/recipe/medkit_from_{food}_{k}.json", obj)
        medkits.append((food, n, k, outn))

# Ванильные кирасы перекрыты броней SL (в мосте броня берётся из инвентаря SL)
put("data/minecraft/recipe/iron_chestplate.json", out["data/slbridge/recipe/armorlight.json"])
put("data/minecraft/recipe/diamond_chestplate.json", out["data/slbridge/recipe/armorcombat.json"])
del out["data/slbridge/recipe/armorlight.json"]; del out["data/slbridge/recipe/armorcombat.json"]

# ── проверка конфликтов с ванильными рецептами и между собой ──
TAGS = {}
def tag_items(t, seen=None):
    if not VANILLA: return {t}
    seen = seen or set()
    if t in seen: return set()
    seen.add(t)
    f = os.path.join(VANILLA, '..', 'tags', 'item', t[1:].split(':')[-1] + '.json')
    if not os.path.exists(f): return {t}
    r = set()
    for v in json.load(open(f))['values']:
        v = v if isinstance(v, str) else v['id']
        r |= tag_items(v, seen) if v.startswith('#') else {v}
    return r
def expand(v):
    vs = v if isinstance(v, list) else [v]
    r = set()
    for x in vs: r |= tag_items(x) if x.startswith('#') else {x}
    return frozenset(r)
def norm_shaped(pattern, keymap):
    rows = [r for r in pattern]
    def cell(c):
        if c == ' ': return None
        return expand(keymap[c])
    g = tuple(tuple(cell(c) for c in r) for r in rows)
    return g
def mirror(g): return tuple(tuple(reversed(r)) for r in g)
def cells_match(a, b):
    if len(a) != len(b) or any(len(x) != len(y) for x, y in zip(a, b)): return False
    for ra, rb in zip(a, b):
        for ca, cb in zip(ra, rb):
            if (ca is None) != (cb is None): return False
            if ca is not None and not (ca & cb): return False
    return True
def ms(lst): return sorted(tuple(sorted(expand(x))) for x in lst)

mine = {p: o for p, o in out.items()}
problems = []
pool = list(mine.items())
if VANILLA:
    for f in glob.glob(os.path.join(VANILLA, '*.json')):
        name = 'data/minecraft/recipe/' + os.path.basename(f)
        if name in mine: continue
        pool.append((name, json.load(open(f))))
for i, (pa, a) in enumerate(pool):
    if not pa.startswith('data/slbridge') and pa not in ("data/minecraft/recipe/iron_chestplate.json","data/minecraft/recipe/diamond_chestplate.json"): continue
    for pb, b in pool:
        if pb == pa or a['type'] != b['type']: continue
        if pb.startswith('data/slbridge') and pb < pa: continue
        if a['type'] == 'minecraft:crafting_shaped':
            ga = norm_shaped(a['pattern'], a['key']); gb = norm_shaped(b['pattern'], b['key'])
            if cells_match(ga, gb) or cells_match(ga, mirror(gb)): problems.append((pa, pb))
        elif a['type'] == 'minecraft:crafting_shapeless':
            if ms(a['ingredients']) == ms(b['ingredients']): problems.append((pa, pb))
if problems:
    for p in problems: print('КОНФЛИКТ:', p)
    sys.exit(1)

for p, o in out.items():
    fp = os.path.join(RES, p); os.makedirs(os.path.dirname(fp), exist_ok=True)
    json.dump(o, open(fp, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print(len(out), 'рецептов записано')

# ── таблица для лаунчера (C#) ──
def cs(s): return '"' + s.replace('\\', '\\\\').replace('"', '\\"') + '"'
L = ['// Сгенерировано tools/gen_recipes.py — не править руками.', 'namespace SlBridge.Launcher', '{',
     '    internal static class RecipeData', '    {',
     '        public sealed record Ing(string Label, string Color);',
     '        public sealed record Rec(string Group, string Name, bool Shaped, string[] Grid, string Note);',
     '        public static readonly System.Collections.Generic.Dictionary<char, Ing> Legend = new()', '        {']
for c, (i, lab, col) in LEG.items(): L.append(f"            [{repr(c)}] = new({cs(lab)}, {cs(col)}),")
L += ['        };', '        public static readonly Rec[] All =', '        {']
for t in table:
    grid = t['grid'] if t['shaped'] else [''.join(c if len(c) == 1 else '?' for c in t['items'])]
    note = ''
    if not t['shaped'] and any(len(c) > 1 for c in t['items']):
        note = {'Adrenaline': 'Золотое яблоко', 'SCP500': 'Зачарованное золотое яблоко'}.get(t['key'], '')
    L.append(f"            new({cs(t['group'])}, {cs(t['name'])}, {'true' if t['shaped'] else 'false'}, new[] {{ {', '.join(cs(r) for r in grid)} }}, {cs(note)}),")
L += ['        };', '        public static readonly (string Food, int Hunger, int Need, int Out)[] Medkits =', '        {']
for food, n, k, o in medkits: L.append(f"            ({cs(FOOD_RU[food])}, {n}, {k}, {o}),")
L += ['        };', '    }', '}']
open(os.path.join(LAUNCHER, 'RecipeData.g.cs'), 'w', encoding='utf-8').write('\n'.join(L) + '\n')

# ── фрагмент README ──
R = []
def grid_md(rows):
    rows = [r.ljust(3) for r in rows] + ['   '] * (3 - len(rows))
    sym = lambda c: '·' if c == ' ' else c
    return '\n'.join('    ' + ' '.join(sym(c) for c in r) for r in rows)
groups = []
for t in table:
    if t['group'] not in groups: groups.append(t['group'])
R.append('Все предметы SL крафтятся на верстаке Minecraft (или через быстрый крафт в меню Server-specific). Готовый предмет сразу становится настоящим предметом SCP:SL в инвентаре; патроны добавляются по 80 штук. Форму можно отражать по горизонтали, а маленькие формы ставить в любое место сетки. Ванильные железная и алмазная кирасы заменены лёгкой и боевой бронёй SL (в мосте броня Minecraft всё равно берётся из брони SL). Рисунки есть и в лаунчере, вкладка «⚒ Крафты».\n')
R.append('Обозначения: ' + ', '.join(f'`{c}` — {lab}' for c, (i, lab, col) in LEG.items() if any(c in ''.join(t.get("grid", t.get("items", []))) for t in table)) + ', `·` — пусто.\n')
def rows3(rows):
    rows = [r.ljust(3) for r in rows] + ['   '] * (3 - len(rows))
    return [' '.join('·' if c == ' ' else c for c in r) for r in rows]
for g in groups:
    R.append(f'\n#### {g}\n')
    shaped = [x for x in table if x['group'] == g and x['shaped']]
    for i in range(0, len(shaped), 3):
        chunk = shaped[i:i + 3]
        w = max(len(t['name']) for t in chunk) + 4
        lines = [''.join(t['name'].ljust(w) for t in chunk).rstrip()]
        grids = [rows3(t['grid']) for t in chunk]
        for y in range(3): lines.append(''.join(gr[y].ljust(w) for gr in grids).rstrip())
        R.append('```\n' + '\n'.join(lines) + '\n```')
    for t in [x for x in table if x['group'] == g and not x['shaped']]:
        parts = [LEG[c][1] if len(c) == 1 else {'minecraft:golden_apple': 'Золотое яблоко', 'minecraft:enchanted_golden_apple': 'Зачарованное золотое яблоко'}[c] for c in t['items']]
        R.append(f"- **{t['name']}** — в любом порядке: {' + '.join(parts)}")
R.append('\n#### Аптечки из еды\n\n10 единиц голода = 1 аптечка. Кладёшь в сетку несколько одинаковых продуктов в любом порядке:\n')
R.append('| Еда | Голод | Сколько положить | Аптечек |\n|---|---|---|---|')
for food, n, k, o in medkits: R.append(f'| {FOOD_RU[food]} | {n} | {k} | {o} |')
open(README, 'w', encoding='utf-8').write('\n'.join(R) + '\n')
