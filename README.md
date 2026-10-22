# SCP:SL × Minecraft

**Авторы: Claude x Vamatrik**

Две игры работают **одновременно** и каждая рисует своё:

* **Minecraft 26.3 (Fabric)** — мир, блоки, мобы, свет, небо, Незер, Энд, дракон;
* **SCP: Secret Laboratory** — твой персонаж, движение, оружие в руках, инвентарь, интерфейс, админ-панель (M).

Мост связывает логику: ходишь по блокам Minecraft с физикой SL, стреляешь — ломаешь блоки и ранишь мобов,
мобы бьют тебя (урон ×5 под 100 HP SL), подбираешь предметы на E, крафтишь в настоящем верстаке, плавишь в печке,
ходишь в Незер и Энд. **Клиент SCP:SL не модифицируется** — это обычная игра из Steam.

## Как это устроено

```
 SCP:SL (Steam, без изменений) ──игра── Dedicated Server + плагин LabAPI ──TCP 25599── Minecraft + мод Fabric
            │                                                                              │
            └──────── окно ─────►  оверлей (Windows Graphics Capture)  ◄────── окно ───────┘
```

| Часть | Папка | Что делает |
|---|---|---|
| Плагин SL (LabAPI) | `src/sl-plugin` | невидимые коллизии по блокам Minecraft, поза камеры, выстрелы, урон, предметы-стаки, меню, команды |
| Мод Minecraft (Fabric) | `src/fabric` | камера = камера SL, попадания, коллизии, предметы, окна MC, порталы, падение, броня |
| Оверлей | `src/compositor` | захват окон SL и Minecraft; где SL видит зелёный фон-ключ — подставляет Minecraft. Ввод идёт прямо в SL |
| Лаунчер | `src/launcher` | подготовка (поиск игр, установка мода/плагина), выбор/создание мира, запуск и остановка всего |

Протокол между частями — [`docs/PROTOCOL.md`](docs/PROTOCOL.md).

## Играть

1. Скачай архив из [Releases](../../releases), распакуй, запусти `SlBridgeLauncher.exe`.
2. Вкладка **«Подготовка»** — пройди шаги: SCP:SL и SCP:SL Dedicated Server из Steam (одной версии),
   Minecraft 26.3 + Fabric (удобнее через [Prism Launcher](https://prismlauncher.org)), кнопка «Установить мод».
3. Вкладка **«Играть»** — выбери или создай мир, нажми «Играть». В SL: Play → Direct connect → Ctrl+V.

Если оверлей что-то перекрывает: **Ctrl+Shift+F12** — выключить/включить, **Ctrl+Shift+F11** — показать маску.

## Управление

* Всё, что в SCP:SL: ходьба, бег, прыжок, оружие, инвентарь (Tab), админ-панель (M).
* Стрельба по блоку — ломает его (твёрже блок — больше попаданий); по мобу — урон.
* Зажать **E** на предмете — подобрать; **Q** — выбросить стак.
* **E** (взаимодействие) — поставить блок, дверь/рычаг, еда, огниво/ведро/око Эндера, верстак/печь/сундук (окно Minecraft).
* **I** — инвентарь Minecraft с крафтом 2×2. Клавиши меняются в SL: Esc → Настройки → Server-specific.
* Консоль SL (`~`): `.mc time set day`; в админ-панели: `mc give @s diamond 5`.

## Сборка из исходников

Нужны: .NET SDK 8+, JDK 25, Windows (для оверлея и лаунчера — Windows 10 1903+).

```powershell
# плагин SL (берёт библиотеки игры из SCP:SL Dedicated Server в Steam; другой путь: -p:SLManaged=...)
dotnet build -c Release src/sl-plugin

# мод Minecraft
cd src/fabric; ./gradlew build; cd ../..          # → src/fabric/build/libs/slbridge-fabric-*.jar

# оверлей и лаунчер
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true src/compositor
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true src/launcher
```

Раскладка для лаунчера: рядом с `SlBridgeLauncher.exe` — папка `dist/` с `SlBridge.dll`, `slbridge-fabric-*.jar`,
`fabric-api-*.jar` ([Modrinth](https://modrinth.com/mod/fabric-api)) и `compositor/SlBridgeCompositor.exe`.

## Безопасность

* Клиент SCP:SL и античит не трогаются: оверлей только **захватывает изображение окон** (Windows Graphics Capture)
  и рисует поверх, клики проходят сквозь него.
* Сеть: только `127.0.0.1` (мост между плагином и модом, порт 25599) и локальный сервер SL (7777).
* Все бинарники в релизе собираются из этого репозитория — можно проверить на VirusTotal или собрать самому.

## Авторы

**Claude x Vamatrik** — идея, тесты и направление — Vamatrik; архитектура и код — Claude (Anthropic).
