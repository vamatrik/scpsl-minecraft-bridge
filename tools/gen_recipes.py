#!/usr/bin/env python3
"""Генерирует рецепты предметов SCP:SL для мода (datapack), таблицу для лаунчера и README.
Запуск: python3 gen_recipes.py <fabric_resources_dir> <launcher_dir> <readme_fragment.md> [vanilla_recipe_dir]"""
import json, os, sys, math, glob

RES, LAUNCHER, README = sys.argv[1], sys.argv[2], sys.argv[3]  # README: префикс, пишутся <README>.ru.md и <README>.en.md
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
    'J': ('minecraft:coal',        'Уголь',           '#2B2B2B'),
    'r': ('minecraft:redstone_block', 'Блок редстоуна', '#A01414'),
    'w': ('',                      'Меч (любой материал)', '#B0B0B0'),
    'p': ('',                      'Кирка того же материала', '#8C8C8C'),
    '*': ('',                      'Этот же предмет (разряженный)', '#3FA34D'),
}
LEG_EN = {'#': 'Iron ingot', 'B': 'Iron block', 'D': 'Diamond', 'N': 'Netherite ingot', 'G': 'Gunpowder', 'S': 'Stick', 'R': 'Redstone', 'T': 'Torch', 'X': 'TNT', 'L': 'Glowstone dust', 'U': 'Sugar', 'O': 'Glass bottle', 'C': 'Cocoa beans', 'E': 'Spider eye', 'M': 'Slimeball', 'H': 'Leather cap', 'P': 'Ender pearl', 'F': 'Flower pot', 'I': 'Packed ice', 'Y': 'Blue ice', 'W': 'Bowl', 'K': 'Note block', 'V': 'Eye of ender', 'Q': 'Glass pane', 'A': 'Glass', 'Z': 'Copper ingot', 'J': 'Coal', 'r': 'Redstone block', 'w': 'Sword (any material)', 'p': 'Pickaxe of the same material', '*': 'The same item (used)'}

# (ключ ItemType SL, имя, иконка, группа, форма|список для бесформенных, кол-во)
SHAPED = [
    # ── оружие: только железные слитки ──
    ('GunCOM15',   'COM-15',            'crossbow', 'Оружие', ['##', 'Z '], 1),
    ('GunCOM18',   'COM-18',            'crossbow', 'Оружие', ['###', '#  '], 1),
    ('GunCom45',   'COM-45',            'crossbow', 'Оружие', ['###', '## '], 1),
    ('GunRevolver','.44 Револьвер',     'crossbow', 'Оружие', ['###', ' # '], 1),
    ('GunFSP9',    'FSP-9',             'crossbow', 'Оружие', ['###', '#  ', '#  '], 1),
    ('GunCrossvec','Crossvec',          'crossbow', 'Оружие', ['###', '###', '#  '], 1),
    ('GunE11SR',   'MTF-E11-SR',        'crossbow', 'Оружие', ['#R#', '###', ' # '], 1),
    ('GunAK',      'AK',                'crossbow', 'Оружие', ['###', '## ', '#  '], 1),
    ('GunA7',      'A7',                'crossbow', 'Оружие', ['###', ' ##', ' # '], 1),
    ('GunShotgun', 'Дробовик',          'crossbow', 'Оружие', ['###', '###', '# #'], 1),
    ('GunLogicer', 'Logicer',           'crossbow', 'Оружие', ['###', '###', '#B '], 1),
    ('GunFRMG0',   'FR-MG-0',           'crossbow', 'Оружие', ['###', '# #', '## '], 1),
    # ── особое оружие ──
    ('GunSCP127',  'SCP-127',           'crossbow', 'Особое оружие', ['##D', '#E '], 1),
    ('ParticleDisruptor', 'Молекулярный разрушитель', 'trident', 'Особое оружие', ['##B', '#  '], 1),
    ('MicroHID',   'Micro H.I.D.',      'lightning_rod', 'Особое оружие', ['###', 'DDD', '###'], 1),
    ('Jailbird',   'Дубинка (Jailbird)','mace',     'Особое оружие', ['D', 'r', '#'], 1),
    ('SCP1509',    'Мачете (SCP-1509)', 'iron_sword','Особое оружие', ['  D', ' N ', 'S  '], 1),
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
    ('Lantern',    'Лампа',             'lantern',  'Снаряжение', ['JJJ', 'SSS', 'JJJ'], 1),
]
SHAPELESS = [
    ('GrenadeHE',   'Осколочная граната', 'fire_charge', 'Снаряжение', ['#', 'X'], 1),
    ('GrenadeFlash','Светошумовая граната','glowstone_dust','Снаряжение', ['#', 'L', 'G'], 1),
    ('Painkillers', 'Обезболивающее',     'bone_meal',  'Медицина', ['U', 'U', 'O'], 1),
    ('Adrenaline',  'Адреналин',          'experience_bottle', 'Медицина', ['U', 'O', 'minecraft:glistering_melon_slice'], 1),
    ('SCP500',      'SCP-500 (панацея)',  'redstone',   'Медицина', ['minecraft:golden_apple'], 1),
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

NAMES_EN = {'GunCOM15': 'COM-15', 'GunCOM18': 'COM-18', 'GunCom45': 'COM-45', 'GunRevolver': '.44 Revolver', 'GunFSP9': 'FSP-9', 'GunCrossvec': 'Crossvec', 'GunE11SR': 'MTF-E11-SR', 'GunAK': 'AK', 'GunA7': 'A7', 'GunShotgun': 'Shotgun', 'GunLogicer': 'Logicer', 'GunFRMG0': 'FR-MG-0', 'GunSCP127': 'SCP-127', 'ParticleDisruptor': 'Particle Disruptor', 'MicroHID': 'Micro H.I.D.', 'Jailbird': 'Jailbird', 'SCP1509': 'Machete (SCP-1509)', 'Ammo9x19': '9x19 ammo (80)', 'Ammo556x45': '5.56x45 ammo (80)', 'Ammo762x39': '7.62x39 ammo (80)', 'Ammo12gauge': '12 gauge ammo (80)', 'Ammo44cal': '.44 ammo (80)', 'ArmorLight': 'Light Armor', 'ArmorCombat': 'Combat Armor', 'ArmorHeavy': 'Heavy Armor', 'Radio': 'Radio', 'Flashlight': 'Flashlight', 'SCP1344': 'SCP-1344 (goggles)', 'Lantern': 'Lantern', 'GrenadeHE': 'Frag Grenade', 'GrenadeFlash': 'Flashbang', 'Painkillers': 'Painkillers', 'Adrenaline': 'Adrenaline', 'SCP500': 'SCP-500 (panacea)', 'SCP207': 'SCP-207', 'AntiSCP207': 'Anti-SCP-207', 'SCP018': 'SCP-018 (ball)', 'SCP268': 'SCP-268 (cap)', 'SCP244a': 'SCP-244-A (vase)', 'SCP244b': 'SCP-244-B (vase)', 'SCP1853': 'SCP-1853', 'SCP2176': 'SCP-2176 (bulb)', 'SCP330': 'SCP-330 (candy)', 'SCP1576': 'SCP-1576 (gramophone)', 'Medkit': 'Medkit'}
GROUPS_EN = {'Перезарядка':'Recharge','Оружие': 'Weapons', 'Особое оружие': 'Special weapons', 'Патроны': 'Ammo', 'Броня': 'Armor', 'Снаряжение': 'Equipment', 'SCP': 'SCP', 'Медицина': 'Medical'}

# еда -> голод (сколько единиц восстанавливает); 10 единиц = 1 аптечка
FOOD = {
 'apple':4,'baked_potato':5,'beef':3,'beetroot':1,'beetroot_soup':6,'bread':5,'carrot':3,'chicken':2,
 'chorus_fruit':4,'cod':2,'cooked_beef':8,'cooked_chicken':6,'cooked_cod':5,'cooked_mutton':6,
 'cooked_porkchop':8,'cooked_rabbit':5,'cooked_salmon':6,'cookie':2,'dried_kelp':1,'glow_berries':2,
 'golden_carrot':6,'honey_bottle':6,'melon_slice':2,'mushroom_stew':6,'mutton':2,'poisonous_potato':2,
 'porkchop':3,'potato':1,'pufferfish':1,'pumpkin_pie':8,'rabbit':3,'rabbit_stew':10,'rotten_flesh':4,
 'salmon':2,'spider_eye':2,'suspicious_stew':6,'sweet_berries':2,'tropical_fish':1,
}
FOOD_EN = {
 'apple':'Apple','baked_potato':'Baked potato','beef':'Raw beef','beetroot':'Beetroot','beetroot_soup':'Beetroot soup','bread':'Bread','carrot':'Carrot','chicken':'Raw chicken','chorus_fruit':'Chorus fruit','cod':'Raw cod','cooked_beef':'Steak','cooked_chicken':'Cooked chicken','cooked_cod':'Cooked cod','cooked_mutton':'Cooked mutton','cooked_porkchop':'Cooked porkchop','cooked_rabbit':'Cooked rabbit','cooked_salmon':'Cooked salmon','cookie':'Cookie','dried_kelp':'Dried kelp','glow_berries':'Glow berries','golden_carrot':'Golden carrot','honey_bottle':'Honey bottle','melon_slice':'Melon slice','mushroom_stew':'Mushroom stew','mutton':'Raw mutton','poisonous_potato':'Poisonous potato','porkchop':'Raw porkchop','potato':'Potato','pufferfish':'Pufferfish','pumpkin_pie':'Pumpkin pie','rabbit':'Raw rabbit','rabbit_stew':'Rabbit stew','rotten_flesh':'Rotten flesh','salmon':'Raw salmon','spider_eye':'Spider eye','suspicious_stew':'Suspicious stew','sweet_berries':'Sweet berries','tropical_fish':'Tropical fish',
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

LANG_RU, LANG_EN = {}, {}
def result(key, name, icon, count):
    tkey = "item.slbridge." + key.lower()
    LANG_RU[tkey] = name; LANG_EN[tkey] = NAMES_EN[key]
    return {"id": "minecraft:trial_key", "count": count, "components": {  # ключ испытаний: не блок (не ставится), ни в одном рецепте, сам ничего не делает
        "minecraft:custom_data": {"slItem": key},
        "minecraft:item_name": {"translate": tkey, "fallback": NAMES_EN[key]},
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

# Перезарядка: предмет в центре, вокруг 8 блоков редстоуна → тот же предмет «как новый» (полный заряд/магазин, без износа).
# Центр — ингредиент Fabric «custom_data»: подходит наша бумага-заглушка с меткой slItem (заряд не важен).
RECHARGE = [('MicroHID', 'Micro H.I.D. — зарядка', 'Micro H.I.D. — recharge'),
            ('Jailbird', 'Дубинка — починка', 'Jailbird — repair'),
            ('ParticleDisruptor', 'Молекулярный разрушитель — зарядка', 'Particle Disruptor — recharge')]
for key, ru, en in RECHARGE:
    base = next(x for x in SHAPED if x[0] == key)
    obj = {"type": "minecraft:crafting_shaped", "category": "equipment", "pattern": ["rrr", "r*r", "rrr"],
           "key": {"r": "minecraft:redstone_block",
                   "*": {"fabric:type": "fabric:custom_data", "base": ["minecraft:trial_key", "minecraft:barrier", "minecraft:paper"], "nbt": {"slItem": key}}},
           "result": result(key, base[1], base[2], 1)}
    put(f"data/slbridge/recipe/recharge_{key.lower()}.json", obj)
    table.append(dict(key=key, name=ru, name_en=en, group='Перезарядка', shaped=True, grid=["rrr", "r*r", "rrr"], legend=['r', '*'], count=1))

# Панацея ×8 из зачарованного золотого яблока
obj = {"type": "minecraft:crafting_shapeless", "category": "misc", "ingredients": ["minecraft:enchanted_golden_apple"],
       "result": result("SCP500", "SCP-500 (панацея)", "redstone", 8)}
put("data/slbridge/recipe/scp500_x8.json", obj)
table.append(dict(key='SCP500', name='SCP-500 (панацея) ×8', name_en='SCP-500 (panacea) ×8', group='Медицина', shaped=False, items=['minecraft:enchanted_golden_apple'], count=8))

# Мультитулы: меч + кирка одного материала → мачете SL с уровнем (урон и скорость добычи зависят от материала)
TIERS = [('wooden', 'wood', 'Деревянный мультитул', 'Wooden multitool'), ('stone', 'stone', 'Каменный мультитул', 'Stone multitool'),
         ('copper', 'copper', 'Медный мультитул', 'Copper multitool'), ('iron', 'iron', 'Железный мультитул', 'Iron multitool'),
         ('golden', 'gold', 'Золотой мультитул', 'Golden multitool'), ('diamond', 'diamond', 'Алмазный мультитул', 'Diamond multitool'),
         ('netherite', 'netherite', 'Незеритовый мультитул', 'Netherite multitool')]
for mat, tier, ru, en in TIERS:
    tkey = "item.slbridge.multitool_" + tier
    LANG_RU[tkey] = ru; LANG_EN[tkey] = en
    res = {"id": "minecraft:trial_key", "count": 1, "components": {
        "minecraft:custom_data": {"slItem": "SCP1509", "slState": "tier=" + tier},
        "minecraft:item_name": {"translate": tkey, "fallback": en},
        "minecraft:item_model": f"minecraft:{mat}_sword",
        "minecraft:enchantment_glint_override": True, "minecraft:rarity": "rare"}}
    obj = {"type": "minecraft:crafting_shapeless", "category": "equipment",
           "ingredients": [f"minecraft:{mat}_sword", f"minecraft:{mat}_pickaxe"], "result": res}
    put(f"data/slbridge/recipe/multitool_{tier}.json", obj)
table.append(dict(key='SCP1509', name='Мультитул (мачете SL): дерево, камень, медь, железо, золото, алмаз, незерит', name_en='Multitool (SL machete): wood, stone, copper, iron, gold, diamond, netherite',
                  group='Особое оружие', shaped=False, items=['w', 'p'], count=1))

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
    if isinstance(v, dict): return frozenset(['custom:' + json.dumps(v, sort_keys=True)])
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

# ── языковые файлы мода ──
for code, d in (('ru_ru', LANG_RU), ('en_us', LANG_EN)):
    fp = os.path.join(RES, 'assets', 'slbridge', 'lang', code + '.json'); os.makedirs(os.path.dirname(fp), exist_ok=True)
    json.dump(d, open(fp, 'w', encoding='utf-8'), ensure_ascii=False, indent=1, sort_keys=True)

# ── таблица для лаунчера (C#) ──
APPLES = {'minecraft:golden_apple': ('Золотое яблоко', 'Golden apple'), 'minecraft:enchanted_golden_apple': ('Зачарованное золотое яблоко', 'Enchanted golden apple'),
          'minecraft:glistering_melon_slice': ('Сверкающий ломтик арбуза', 'Glistering melon slice')}
def cs(s): return '"' + s.replace('\\', '\\\\').replace('"', '\\"') + '"'
L = ['// Сгенерировано tools/gen_recipes.py — не править руками.', 'namespace SlBridge.Launcher', '{',
     '    internal static class RecipeData', '    {',
     '        public sealed record Ing(string Ru, string En, string Color);',
     '        public sealed record Rec(string GroupRu, string GroupEn, string Ru, string En, bool Shaped, string[] Grid, string NoteRu, string NoteEn);',
     '        public static readonly System.Collections.Generic.Dictionary<char, Ing> Legend = new()', '        {']
for c, (i, lab, col) in LEG.items(): L.append(f"            [{repr(c)}] = new({cs(lab)}, {cs(LEG_EN[c])}, {cs(col)}),")
L += ['        };', '        public static readonly Rec[] All =', '        {']
for t in table:
    grid = t['grid'] if t['shaped'] else [''.join(c if len(c) == 1 else '?' for c in t['items'])]
    nr = ne = ''
    if not t['shaped']:
        for c in t['items']:
            if c in APPLES: nr, ne = APPLES[c]
    L.append(f"            new({cs(t['group'])}, {cs(GROUPS_EN[t['group']])}, {cs(t['name'])}, {cs(t.get('name_en') or NAMES_EN[t['key']])}, {'true' if t['shaped'] else 'false'}, new[] {{ {', '.join(cs(r) for r in grid)} }}, {cs(nr)}, {cs(ne)}),")
L += ['        };', '        public static readonly (string Ru, string En, int Hunger, int Need, int Out)[] Medkits =', '        {']
for food, n, k, o in medkits: L.append(f"            ({cs(FOOD_RU[food])}, {cs(FOOD_EN[food])}, {n}, {k}, {o}),")
L += ['        };', '    }', '}']
open(os.path.join(LAUNCHER, 'RecipeData.g.cs'), 'w', encoding='utf-8').write('\n'.join(L) + '\n')

# ── названия и иконки предметов SL для плагина (выпадение при смерти) ──
ICONS = {}
for key, name, icon, *_ in SHAPED + SHAPELESS: ICONS[key] = (name, NAMES_EN[key], icon)
ICONS['Medkit'] = ('Аптечка', 'Medkit', 'red_dye')
P = ['// Сгенерировано tools/gen_recipes.py — не править руками.', 'using System.Collections.Generic;', '', 'namespace SlBridge', '{',
     '    /// <summary>Название (ru/en) и иконка Minecraft для предметов SL, которые лежат в мире Minecraft.</summary>',
     '    public static class SlItemNames', '    {',
     '        public static readonly Dictionary<string, (string Ru, string En, string Icon)> All = new Dictionary<string, (string, string, string)>', '        {']
for k, (ru, en, ic) in sorted(ICONS.items()):
    if k.startswith('Ammo'): ru, en = ru.replace(' (80)', ''), en.replace(' (80)', '')
    P.append(f'            ["{k}"] = ({cs(ru)}, {cs(en)}, "{ic}"),')
P += ['        };', '    }', '}']
open(os.path.join(LAUNCHER, '..', 'sl-plugin', 'SlItemNames.g.cs'), 'w', encoding='utf-8').write('\n'.join(P) + '\n')

# ── фрагменты README (ru и en) ──
TXT = {
 'ru': dict(intro='Все предметы SL крафтятся на верстаке Minecraft (E по верстаку). Готовый предмет сразу становится настоящим предметом SCP:SL в инвентаре; патроны добавляются по 80 штук. Форму можно отражать по горизонтали, а маленькие формы ставить в любое место сетки. Ванильные железная и алмазная кирасы заменены лёгкой и боевой бронёй SL (в мосте броня Minecraft всё равно берётся из брони SL). В темноте Minecraft темно по-настоящему — скрафти лампу и держи её включённой в руке (или фонарик — он светит узким лучом туда, куда смотришь). В окнах Minecraft видны и твои предметы SL — Micro H.I.D., дубинку и молекулярный разрушитель можно перезарядить на верстаке. Рисунки есть и в лаунчере, вкладка «⚒ Крафты».\n',
            legend='Обозначения', empty='пусто', anyorder='в любом порядке', medkits='Аптечки из еды',
            medtext='10 единиц голода = 1 аптечка. Кладёшь в сетку несколько одинаковых продуктов в любом порядке:\n',
            head='| Еда | Голод | Сколько положить | Аптечек |'),
 'en': dict(intro='Every SL item is crafted on a Minecraft crafting table (E on the table). The result immediately becomes a real SCP:SL item in your inventory; ammo is added 80 at a time. Shapes can be mirrored horizontally, and small shapes can go anywhere in the grid. The vanilla iron and diamond chestplates are replaced by SL light and combat armor (in the bridge, Minecraft armor comes from your SL armor anyway). Darkness in Minecraft is real — craft a lantern and hold it lit in your hand (or a flashlight — it shines a narrow beam where you look). Your SL items show up in Minecraft windows too — the Micro H.I.D., Jailbird and Particle Disruptor can be recharged on a crafting table. The drawings are also in the launcher, “⚒ Crafting” tab.\n',
            legend='Legend', empty='empty', anyorder='in any order', medkits='Medkits from food',
            medtext='10 hunger points = 1 medkit. Put several identical food items into the grid in any order:\n',
            head='| Food | Hunger | How many | Medkits |'),
}
def rows3(rows):
    rows = [r.ljust(3) for r in rows] + ['   '] * (3 - len(rows))
    return [' '.join('·' if c == ' ' else c for c in r) for r in rows]
groups = []
for t in table:
    if t['group'] not in groups: groups.append(t['group'])
used_chars = set(''.join(''.join(t.get('grid', [])) + ''.join(c for c in t.get('items', []) if len(c) == 1) for t in table))
for lang, X in TXT.items():
    en = lang == 'en'
    nm = (lambda t: t.get('name_en') or NAMES_EN[t['key']]) if en else (lambda t: t['name'])
    lab = (lambda c: LEG_EN[c]) if en else (lambda c: LEG[c][1])
    R = [X['intro']]
    R.append(X['legend'] + ': ' + ', '.join(f'`{c}` — {lab(c)}' for c in LEG if c in used_chars) + f", `·` — {X['empty']}.\n")
    for g in groups:
        R.append(f"\n### {GROUPS_EN[g] if en else g}\n")
        shaped = [x for x in table if x['group'] == g and x['shaped']]
        for i in range(0, len(shaped), 3):
            chunk = shaped[i:i + 3]
            w = max(len(nm(t)) for t in chunk) + 4
            lines = [''.join(nm(t).ljust(w) for t in chunk).rstrip()]
            grids = [rows3(t['grid']) for t in chunk]
            for y in range(3): lines.append(''.join(gr[y].ljust(w) for gr in grids).rstrip())
            R.append('```\n' + '\n'.join(lines) + '\n```')
        for t in [x for x in table if x['group'] == g and not x['shaped']]:
            parts = [lab(c) if len(c) == 1 else APPLES[c][1 if en else 0] for c in t['items']]
            R.append(f"- **{nm(t)}** — {X['anyorder']}: {' + '.join(parts)}")
    R.append(f"\n### {X['medkits']}\n\n{X['medtext']}")
    R.append(X['head'] + '\n|---|---|---|---|')
    for food, n, k, o in medkits: R.append(f'| {(FOOD_EN if en else FOOD_RU)[food]} | {n} | {k} | {o} |')
    open(f'{README}.{lang}.md', 'w', encoding='utf-8').write('\n'.join(R) + '\n')
