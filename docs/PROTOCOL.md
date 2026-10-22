# Протокол моста SCP:SL ↔ Minecraft (версия 2)

TCP, `127.0.0.1:25599`. Сервер — мод Minecraft, клиент — плагин SCP:SL.
Одно сообщение — один JSON-объект на строке (`\n`). Поле `t` — тип.
**Все координаты — в системе Minecraft**; перевод в Unity делает плагин SL (`CoordMap.cs`).
Предмет (`item`) — `{id, count, name, max}`.

## SL → Minecraft

| t | поля | смысл |
|---|------|-------|
| `hello` | `ver`, `side` | рукопожатие |
| `resync` | — | SL сбросил коллизии — прислать все ячейки заново |
| `pose` | `x,y,z` (глаза), `yaw`, `pitch`, `seq` | поза камеры SL каждый кадр; позы с `seq` меньше последнего телепорта игнорируются |
| `spawnReq` | — | первое появление: «где встать?» |
| `respawnReq` | — | возрождение после смерти (точка возрождения Верхнего мира) |
| `shot` | `x,y,z`, `dx,dy,dz`, `dmg` (урон SL), `range`, `breaks` | выстрел / удар ближнего боя / луч Micro HID |
| `use` | `x,y,z`, `dx,dy,dz`, `held: item\|null` | «взаимодействие» (E): верстак/печь, двери, установка блока, использование предмета, еда |
| `pickup` | `e` (id сущности) | подобрать предмет с земли |
| `drop` | `item`, `x,y,z`, `dx,dy,dz` | выбросить предмет в мир Minecraft |
| `craftList` / `smeltList` | `inv: [item]` | какие рецепты доступны из этого инвентаря |
| `craft` / `smelt` | `rid`, `times`, `inv` | выполнить рецепт |
| `cmd` | `text` | команда Minecraft (права OWNER) |

## Minecraft → SL

| t | поля | смысл |
|---|------|-------|
| `hello` | `ver`, `side` | рукопожатие |
| `spawn` | `ready`, `x,y,z` (ноги), `yaw` | точка первого появления |
| `teleport` | `x,y,z`, `yaw`, `seq`, `dim` | Minecraft сам переместил игрока (портал, жемчуг, /tp, возрождение) |
| `sec` | `k` = `"cx,cy,cz"` (ячейка 8³), `b` = `[x0,y0,z0,x1,y1,z1, …]` | коллизии ячейки |
| `secDel` | `k` | ячейка вышла из радиуса |
| `hurt` | `amount` (уже ×5), `mcAmount`, `source`, `attacker?` | урон игроку (мобы, падение, лава, утопление…) |
| `hit` | `kind`, `dead`… | результат выстрела (для хитмаркера) |
| `items` | `list: [item + e, x,y,z]` | предметы на земле рядом (меняется — шлётся заново) |
| `picked` | `e`, `item?` | подобранный предмет |
| `used` | `open?` (`craft`/`furnace`), `held`, `changed`, `gained: [item]`, `heal?` | итог «взаимодействия» |
| `craftList` | `table`, `recipes: [{rid, result, max}]` | доступные рецепты |
| `smeltList` | `furnace`, `recipes: [{rid, input, result, max}]` | доступная плавка |
| `crafted` | `ok`, `msg?`, `consume: [{id,count}]`, `give: [item]` | итог крафта/плавки |
| `cmdOut` | `cmd`, `lines` | ответ команды |

## Перевод координат

```
unity.x = Origin.x - mc.x      (Unity левосторонняя — ось X отражается)
unity.y = Origin.y + mc.y
unity.z = Origin.z + mc.z
```

## Урон

* Minecraft → SL: ×5 (`damageScale`). Падение считается по правилам Minecraft (вода/сено/слизь), удушье в блоке не пересылается.
* SL → Minecraft: урон оружия из конфига плагина ÷5.
