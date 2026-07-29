# NGU Advisor

NGUAdvisor is an automation platform for the steam version of NGU Idle. It reads your live game state and drives allocation, gear, combat, gold, and every other system toward whatever you're pushing next — with an advisor that explains what it's doing and why.

> **Fair-use / ToS note:** this is a third-party tool that injects into the Steam build of NGU Idle. Use it at your own risk.

**Current release: v1.2.20** — see [CHANGELOG.md](CHANGELOG.md) for what changed.

# The interface

NGU Advisor is one window with a two-level layout: a **primary rail** down the left with the top-level destinations, a **category strip** that opens the pages inside a destination, and the selected screen in the middle. A **status strip** stays pinned across the Advisor pages showing eight live cells (below).

Set it to **Auto** (the master toggle in the status strip's AUTO cell) and the advisor drives every system, explaining each decision as it goes — or open any section to take manual control. Key hotkeys: **F1** opens the window, **F2** pauses/resumes all automation, **F9** opens the Profile Editor (full list [below](#hotkeys)).

## Advisors

Your home base, split into two pages:

- **Overview** — the at-a-glance summary of configuration and current state: state tiles, a growth graph, and the auto-profile the advisor is currently running. At the normal window size Overview uses a vertical scrollbar; everything stays reachable by scrolling down (there is no horizontal scrolling).
- **Priorities** — one prioritized, ordered to-do list spanning every system, with a green **AUTO** badge on anything the advisor already handles for you.

### Status strip

Pinned across the Advisor interface, eight cells give a persistent read of where you are:

| Cell | Meaning |
|---|---|
| **AUTO** | The global automation master (ON/OFF) — the same kill-switch **F2** toggles. |
| **STAGE** | The progression stage the advisor has detected. |
| **PROFILE** | The auto-profile currently applied. |
| **CURRENT GOAL** | What the advisor is working toward right now. |
| **GEAR** | The active gear state / loadout. |
| **REBIRTH** | Rebirth timing and state. |
| **RESOURCES** | A summary of your resource state. |
| **NEXT GOAL** | What comes after the current goal. |

## Profile

The dedicated home for allocation profiles. It shows the **current profile** and its **allocation source/status**, with buttons to **SWITCH** (change which profile is active), **APPLY** (apply the selected profile now), **EDIT** (open the Profile Editor), **FILES** (open the profiles folder), and **REFRESH** (re-read the profile list). New or edited profile files are picked up automatically; **REFRESH** is there if you want to force a re-scan. Profiles from 1.0.0 load unchanged.

## Combat

The current target with idle vs. next-version kill odds, champion toggles, zone routing (Zones / ITOPOD / Blacklist), boost-farm advice, and the combat style row (Combat / Beast Mode / Bosses Only / Fallthrough) — over a live tail of the combat log.

![Combat](media/screenshots/combat.png)

## Economy

Gold in and gold out: zone-snipe state, the Time Machine counterfeit, the titan bank, money-pit tosses with reward prediction, and a gold-drain breakdown (diggers / blood rituals / augments) over a live pit-spin log.

![Economy](media/screenshots/economy.png)

**Money Pit ownership.** Two settings can both stay on at once: **Auto Money Pit** (the standard automatic throw) and **AUTO THROW** (the advisor's pit timing). With AUTO THROW enabled the advisor owns automatic pit timing; with it disabled the standard Auto Money Pit path runs instead — so exactly one automatic path acts at a time and they never compete. **Throw Now** is always an independent, explicit action.

## Systems

Per-system detail, one sub-tab each.

| Yggdrasil — fruit harvest grid | Quests — progress + banking |
|---|---|
| ![Systems Yggdrasil](media/screenshots/systems-yggdrasil.png) | ![Systems Quests](media/screenshots/systems-quests.png) |

| Boosts — order + transforms | Inventory — keep vs. trash |
|---|---|
| ![Systems Boosts](media/screenshots/systems-boosts.png) | ![Systems Inventory](media/screenshots/systems-inventory.png) |

**Blood** — the Iron Pill worthwhile meter, spell routing (Number / Spaghetti / Counterfeit), and every pill / guffin threshold.

![Systems Blood](media/screenshots/systems-blood.png)

## Loadouts

Seven gear sets the advisor manages, each with its own page: **Titan**, **Gold**, **Quest**, **Yggdrasil**, **Cooking**, **Loot Hunter**, and **Shockwave**. Titan / Gold / Quest / Yggdrasil / Loot Hunter are advisor-optimised; **Cooking** and **Shockwave** are manual (you enter the gear IDs, or capture them from your equipped gear).

Each page keeps three things separate so you always know what will happen:

- **the configured loadout** — what the mode is set to (its source and IDs);
- **WILL EQUIP** — the set that will actually be requested at the next swap;
- **CURRENTLY EQUIPPED — SNAPSHOT** — a read-only snapshot of your live gear.

Selecting a mode does **not** equip anything, and neither does **REFRESH**. The **CURRENTLY EQUIPPED** snapshot is captured when the page opens and when you press **REFRESH STATE** — it does not update on its own and it is not a live feed. Only an explicit equip/apply, or an automatic swap the advisor performs, changes your gear.

## Logs

The receipts, filterable and live. **Advisor** is the decision log (why it did what it did), **Loot** is the drop feed, **Session** is the raw running log for deep debugging.

| Advisor | Loot | Session |
|---|---|---|
| ![Logs Advisor](media/screenshots/logs-advisor.png) | ![Logs Loot](media/screenshots/logs-loot.png) | ![Logs Session](media/screenshots/logs-session.png) |

## Settings

The master switchboard — subsystem toggles (Manage / Auto / Swap Gear For / Combat + ITOPOD) plus Misc controls and an **Unload Advisor** button. Turning the top-left master toggle off stops the advisor entirely.

Settings also has a **search box**: start typing and the list filters to the settings that match (try `daily save` or `titan`); clear the box and the full list comes back, and a query with no matches simply shows nothing. Some settings are shown on the system page that owns them rather than in this list.

## Cards

**Cards** handles casting, sort order, and the auto-trash rarity/cost table. **Wishes** sets wish priorities, a blacklist, and the energy / magic / R3 split.

| Cards | Wishes |
|---|---|
| ![Cards](media/screenshots/cards.png) | ![Cards Wishes](media/screenshots/cards-wishes.png) |

## Profile Editor (F9)

A separate window where the auto-profiles are built: time breakpoints along the rebirth, and within each a prioritised list per system (Energy, Magic, R3, Gear, Diggers, Beards, Wandoos+Diff, Misc). The advisor applies the latest breakpoint whose time has passed. The token grammar these lists use is documented under [Allocation](#allocation).

You can open the editor two ways, depending on which window has focus: **PROFILE → EDIT** works while Advisor Settings has focus, and **F9** opens it while the NGU Idle game window has focus (F9 is an in-game shortcut).

Pasting gear IDs into a loadout is safe: the paste is parsed and validated first, you confirm the result before it replaces anything, and invalid or empty input changes nothing. A short single-level **undo** is available right after a paste.

![Profile Editor](media/screenshots/profile-editor.png)

# Instructions

Releases can be found in the [releases section](https://github.com/Glowey-Glow/NGUAdvisor/releases) of the upstream repo. Do not download the "Source code" archive — download the zip with the release version in the name (`dist_1.2.20.zip`, for example). Extract it anywhere, launch NGU Idle, then run `Run NGU Advisor.bat` from the extracted folder.

You'll know injection worked when the overlay appears in the upper-left corner of the game. Open the window with **F1** and start on **Advisors › Overview**.

To update to a new release:

1. In **Settings**, click **Unload Advisor**.
2. Fully restart NGU Idle (the injected DLL is replaced on a fresh game start).
3. Extract the new release, replacing the old extracted files.
4. Start NGU Idle.
5. Run `Run NGU Advisor.bat` again.

Your settings and profiles live in your AppData folder (see [Configuration](#configuration)), not in the release folder, so replacing the extracted files leaves them untouched — don't delete the AppData `NGUAdvisor` folder during an update. Settings and profiles from 1.0.0 onward remain compatible with 1.2.2; no migration is needed.

# Expected behavior

A few things that work as intended:

- **F9** opens the Profile Editor only when the **NGU Idle game window** has focus (it is an in-game shortcut). If Advisor Settings has focus instead, use **PROFILE → EDIT**.
- **Overview** uses a vertical scrollbar at the normal window size — scroll down to reach everything; there is no horizontal scrolling.
- **CURRENTLY EQUIPPED** on a Loadouts page is a snapshot: it updates when you open the page or press **REFRESH STATE**, and it does not update by itself.
- There is no in-app reload button. To reinject (for example after installing a new build), **Unload Advisor**, fully restart NGU Idle, and run `Run NGU Advisor.bat` again.
- Existing settings and profiles need no migration for 1.2.2.

If something isn't working, check **debug.log** in the AppData folder first (see [Configuration](#configuration)).

# Configuration

After injecting the dll, a new folder will be created in your AppData directory called NGUAdvisor (full path is %UserProfile%\AppData\LocalLow\NGUAdvisor). Settings files will be automatically written to this directory. The following files are of interest:

- **settings.json** - Contains settings used by the application. All settings can be configured from the settings form.
- **zoneOverrides.json** - Override the default values for Idle/Manual Power/Toughness for gold sniping.

A profiles folder will be created with the following files:

- **default.json** - Breakpoints for assigning gear, magic, energy, wandoos OS, rebirth time, beards, and diggers. See the allocation section for more information.

More profiles can be added to this folder at your discretion.

A logs folder will also be created. You can read these live in the **Logs** section of the window (Advisor / Loot / Session tabs); they're also written to disk:

- **inject.log** - General advisor/injector activity and decisions.
- **loot.log** - All loot dropped by enemies.
- **combat.log** - Output from the combat algorithm.
- **pitspin.log** - Fruit harvests, money pit, and daily spin results. Not overwritten across sessions.
- **cards.log** - Cast and trashed cards. Not overwritten across sessions.
- **debug.log** - Errors. If something isn't working, check this file first.

Saving settings.json, zoneOverrides.json, or any profile automatically reloads it in the advisor. Reloading the game isn't necessary.

# Settings.json Configurations

Settings in the GUI can be managed directly in the underlying settings file if desired. This can be helpful when pasting in loadouts from the Gear Optimizer. To access these settings, open the **settings.json** file in your AppData NGUAdvisor folder mentioned above.

# Allocation

Allocation profiles can be found in the profiles folder and contain time breakpoints for configuring your gear, beards, diggers, energy allocation, magic allocation and resource 3 allocation. Sample allocation files can be found in the sampleprofile folder.

Browse the bundled [sample profiles](NGUAdvisor/SampleProfiles/) (Normal / Evil / Sadistic) for working examples — the same set ships in each release zip.

The time portion of every breakpoint refers to rebirth time in seconds. Time can be defined as a simple number (ex: 86400) or as a JSON object:

```
"Time": {
    "h": 1,
    "m": 30,
    "s": 20
},
```

Priorities can be modified with an optional percentage cap by adding a : and a percent. As an example:

```
CAPRIT-0:30
```

This will try to cap Poke yourself with a Tack Blood Ritual, but limit the maximum amount allocated to 30% of your current cap. When applied to non-cap priorities, it will limit to % of idle resource. Priorities will respect targets and not allocate to priorities that are target capped.

## Energy

An energy breakpoint is structured as follows:

```
"Energy": [
    {
        "Time": 0,
        "Priorities": ["CAPNGU-0", "CAPWAN", "AT-1", "NGU-1"]
    }
]

```

Priorities come in 2 types - cap and non-cap. Any priority that has -X after it is 0 indexed.

A cap priority will use as much idle energy as possible and hit the highest BB breakpoint possible for the next 10 seconds. A non-cap priority will take a divider of your idle energy based on number of non-cap priorities and attempt to hit the highest BB breakpoint possible for the next 10 seconds. When a priority is calculated, it will push excess energy to later priorities.

In the above example the following actions will be taken:

- NGU-0 (NGU Augments) will be capped
- Wandoos energy will be capped
- Remaining energy will be split between AT-1 (Advanced Training Power) and NGU-1 (NGU Wandoos)

Available cap priorities for Energy are as follows:

- CAPNGU-X (0-8) - Calculate a cap for the NGU.
- CAPALLNGU - Calculate a cap for every NGU starting from 0.
- CAPAT-X (0-4) - Calculate a cap for the AT and attempt to BB it.
- CAPALLAT - Calculate caps for all ATs and BB them.
- CAPBESTAUG - Attempts to allocate to the best augment possible. Before pairs are unlocked, will allocate to highest augment that is easily doable. After will try to optimize.
- CAPWAN - Calculate a cap for wandoos energy.
- CAPTM - Calculate a cap for energy time machine and attempt to BB it.
- CAPBT-X (0-11) - Calculate a cap for basic training.
- CAPALLBT - Caps all basic trainings.
- CAPAUG-X (0-13) - Calculate a cap for augments and attempt to BB it.

Available non-cap priorities for Energy are as follows:

- ALLNGU - Allocate energy to every NGU.
- NGU-X (0-8) - Allocate energy to NGU.
- AT-X (0-4) - Allocate energy to AT.
- ALLAT - Allocate energy to all AT.
- AUG-X (0-13) - Allocate energy to augment.
- BESTAUG - Attempts to allocate to the best augment possible. Before pairs are unlocked, will allocate to highest augment that is easily doable. After will try to optimize.
- WAN - Allocate energy to energy wandoos.
- TM - Allocate energy to energy time machine.
- BT-X (0-11) - Allocate energy to basic training.

More information on allocation indexes can be found on the [wiki](https://github.com/rvazarkar/NGUAdvisor/wiki/Allocation-Indexes)

## Magic

A magic breakpoint is structured as follows:

```
"Magic": [
    {
        "Time": 0,
        "Priorities": ["CAPNGU-0", "CAPWAN", "BR", "NGU-1"]
    }
]

```

Priorities come in 2 types - cap and non-cap. Any priority that has -X after it is 0 indexed.

A cap priority will use as much idle energy as possible and hit the highest BB breakpoint possible for the next 10 seconds. A non-cap priority will take a divider of your idle energy based on number of non-cap priorities and attempt to hit the highest BB breakpoint possible for the next 10 seconds. When a priority is calculated, it will push excess energy to later priorities.

In the above example the following actions will be taken:

- NGU-0 (NGU Yggdrasil) will be capped.
- Wandoos magic will be capped.
- Magic rituals will be capped from highest to lowest, with rituals taking more than 1 hour skipped.
- Remaining magic will be allocated to NGU-1 (NGU Exp).

Available cap priorities for Magic are as follows:

- CAPNGU-X (0-6) - Use the cap button for the NGU.
- CAPALLNGU - Use the cap button for every NGU starting from 0.
- CAPWAN - Use the cap button for wandoos magic.
- CAPTM - Calculate a cap for magic time machine and attempt to BB it.
- CAPRIT-X - Calculate a cap for the ritual and allocate.
- BR - Cast rituals from highest to lowest ignoring rituals you cant afford or will take more than an hour.
- BR-X - Cast rituals from highest to lowest that will finish before time specified by X. BR-3600 will cast rituals that will end before the 1 hour mark from your current time.

Available non-cap priorities for Magic are as follows:

- NGU-X (0-6) - Allocate energy to NGU
- WAN - Allocate energy to magic wandoos
- TM - Allocate energy to magic time machine
- RIT-X - Allocate energy to ritual

More information on allocation indexes can be found on the [wiki](https://github.com/rvazarkar/NGUAdvisor/wiki/Allocation-Indexes)

## R3

An R3 breakpoint is structured as follows:

```
"R3": [
    {
        "Time": 0,
        "Priorities": ["HACK-1"]
    }
]

```

Available priorities for R3 are as follows:

- (CAP)HACK-X (0-14) - Allocate R3 to hacks. Will only ever allocate to the first hack in your priority list that hasn't met its target.
- (CAP)ALLHACK - Adds every hack from 0 to 14 as a priority. Prioritizes hacks with target > 0.

## Gear

A gear breakpoint is structured as follows:

```
"Gear": [
    {
        "Time": 0,
        "ID": [189, 442, 160, 441, 148, 169, 139, 184, 187, 185, 186, 188]
    }
]

```

The list of IDs is the IDs of the gear desired.

You can dump your loadouts from Gear Optimizer using the method found on [this wiki page](https://github.com/rus9384/NGUAdvisor/wiki/Dump-Equipment-from-GO).

## Beards

A beard breakpoint is structured as follows:

```
"Beards": [
    {
        "Time": 0,
        "List": [5, 1, 6, 3]
    }
]
```

The list of Beards is 0 indexed. Fu Manchu is 0, Golden Beard is 6. More information can be found on the [wiki](https://github.com/rus9384/NGUAdvisor/wiki/Allocation-Indexes).

## Diggers

A digger breakpoint is structured as follows:

```
"Diggers": [
    {
        "Time": 3650,
        "List": [8, 3, 4, 5]
    }
]

```

The list of Diggers is 0 indexed. Drop chance Digger is 0, EXP Digger is 11. More information can be found on the [wiki](https://github.com/rus9384/NGUAdvisor/wiki/Allocation-Indexes).

## Wandoos

A wandoos breakpoint is structured as follows:

```
"Wandoos": [
    {
        "Time": 0,
        "OS": 1
    }
]

```

The OS is 0 indexed. Wandoos 98 is 0, Wandoos MEH is 1, Wandoos XL is 2.

## NGU Difficulty

A NGU Difficulty breakpoint is structured as follows:

```
"NGUDiff": [
    {
        "Time": 0,
        "Diff": 0
    }
]
```

The difficulty is 0 indexed. Normal NGUs is 0, Evil NGUs is 1, Sadistic NGUs is 2.

## Rebirth

Rebirth Breakpoints can be written 2 ways. A simple time based breakpoint is written as follows:

```
"RebirthTime": -1
```

A setting of -1 means no rebirths. Otherwise rebirth will be performed when the time in seconds is reached.

More complex rebirth breakpoints can be done as follows:

```
"Rebirth": [
    {
        "Type": "Time",
        "Time": {
            "h": 1,
            "m": 1
        }
    },
    {
        "Type": "Number",
        "Time": {
            "m": 15
        },
        Target: 10000
    }
]
```

The following types are available:

- **Time** - Rebirths when a certain time passes.
- **Number** - Rebirths when your number will be OldNumber \* _Target_.
- **Bosses** - Rebirths when you can defeat _Target_ additional bosses.
- **Muffin** - Rebirths to optimize Muffin consumable usage. Will cycle between 24 hour and (24 hour - _Target_ minutes) Time rebirths. _Target_ must be at least 1 and no more than 60. Enable "Auto Buy Consumables" to auto purchase Muffins if none are available. Will simply behave as a 24 hour Time rebirth without Muffin use if any of the following conditions are met:
  - Currently in a challenge.
  - Able to rebirth into any challenge defined in the Rebirth Breakpoint.
  - Has not yet beaten Sadistic Troll Challenge 2.
  - 5 O'Clock Shadow Perk or Beast Fertilizer Quirk aren't maxed.
  - Do not have any Muffins and cannot buy more (or is configured to not buy more).
- **TimeBalancedMuffin** - Same as the Muffin rebirth except will also add the _Target_ minutes to the 24 hour rebirth to try to keep rebirths at the same time each day. _Target_ must be at least 1 and no more than 15.

The example above will rebirth after 15 minutes if the Number would increase 10000 times, or after 1 hour and 1 minute regardless of Number increase.

The following example will rebirth when your number is 10x your previous number and the rebirth is at least 30 minutes long:

```
"Rebirth": [
    {
        "Type": "Number",
        "Time": {
            "m": 30
        },
        "Target": 10
    }
]
```

The following example will rebirth when you can kill 5 more bosses than your last rebirth:

```
"Rebirth": [
    {
        "Type": "Bosses",
        "Target": 5
    }
]
```

The following example will perform "Muffin" rebirth cycles. If no muffin is active will activate a muffin and rebirth at 24:00:00, followed by a rebirth at 23:00:00:

```
"Rebirth": [
    {
        "Type": "Muffin",
        "Target": 60
    }
]
```

The following example will perform "Muffin" rebirth cycles. If no muffin is active will activate a muffin and rebirth at 24:05:00, followed by a rebirth at 23:55:00:

```
"Rebirth": [
    {
        "Type": "TimeBalancedMuffin",
        "Target": 5
    }
]
```

You can define multiple rebirth points. They are evaluated by time first, and by target later. In the following example if the current rebirth is shorter than 5 minutes, a rebirth will be performed only if you will be able to beat 5 more bosses in next rebirth. But if the current rebirth is longer than 5 minutes, but shorter than 30 minutes, a rebirth will be performed if the number will increase 500 times. And if the current rebirth is longer than 30 minutes, the rebirth will always be performed.

```
"Rebirth": [
    {
        "Type": "Bosses",
        "Target": 5
    },
    {
        "Type": "Number",
        "Time": {
            "m": 5
        },
        Target: 500
    },
    {
        "Type": "Time",
        "Time": {
            "m": 30
        }
    }
]
```

## Challenges

Additionally, you may specify challenges to rebirth into. The following string correspond to challenges:

- **BASIC** - Basic
- **NOAUG** - No Augments
- **24HR** - 24 Hour
- **100LC** - 100 Level Challenge
- **NOEC** - No Equipment
- **TC** - Troll Challenge
- **NORB** - No Rebirth
- **LSC** - Laser Sword Challenge
- **BLIND** - Blind Challenge
- **NONGU** - No NGU
- **NOTM** - No Time Machine

Challenges must be given a number afterwards to specify which challenge is being completed. Challenges start at 1. Ex:

```
"Challenges": ["BASIC-1", "BASIC-2", "TC-1","NOEQ-1"]
```

## Consumables

A Consumables breakpoint is structured as follows:

```
"Consumables": [
    {
        "Time": 0,
        "Items": ["EPOT-B", "MPOT-B"]
    }
]
```

Consumables are indexed as follows:

- **EPOT-A** - Energy Potion alpha
- **EPOT-B** - Energy Potion beta
- **EPOT-C** - Energy Potion delta
- **MPOT-A** - Magic Potion alpha
- **MPOT-B** - Magic Potion beta
- **MPOT-C** - Magic Potion delta
- **R3POT-A** - R3 Potion alpha
- **R3POT-B** - R3 Potion beta
- **R3POT-C** - R3 Potion delta
- **EBARBAR** - Energy Bar Bar
- **MBARBAR** - Magic Bar Bar
- **MUFFIN** - MacGuffin Muffin
- **LC** - Lucky Charm
- **SLC** - Super Lucky Charm
- **MAYO** - Mayo Infuser

Consumables can be modified with an optional number to use by adding a : and a number. As an example:

```
EPOT-A:2
```

This will use two Energy Potion alphas instead of one. Consumables which are active until the next rebirth (beta potions and Muffins) ignore this modification and will only ever consume one.

**IMPORTANT NOTE: BE AWARE OF THE LIMITATIONS OF CONSUMABLE BREAKPOINTS**

The injector cannot remember consumable usage across sessions or know if consumables were used manually. Therefore, if you restart NGU, restart the injector, or make any changes to/reload the profile, it **will** run your current Consumables breakpoint again. The injector will try to compensate by estimating the amount of time a consumable is expected to run and only use consumables when appropriate. But it is not perfect and there are limitations.

**_Example_**:
Let's say you have it set to use a two Lucky Charms 1 hour after rebirth.

If the profile initializes before 01:00:00 into the current rebirth, the breakpoint will execute normally at 01:00:00 and if lucky charm is not currently running, you'll use the two Lucky Charms.

However if the profile is inialized AFTER this point (due to restarting the game or reloading the profile in some way) or the consumable is running when the breakpoint executes, there are a few things which can happen:

- First we determine at what point the Lucky Charms *would* have expired if activated normally. In this case it would be two 30m charms activated at 01:00:00, so the expected end time is 02:00:00
- If the current rebirth time is within 1 minute of the expected end time or later, the consumable will not be used
  - For example, if the profile is loaded at 01:59:00 or later, no lucky charms will be used
- If however the current rebirth time is between the breakpoint time and the expected end time, we check to see if the boost is currently running and get the expected time left in the consumable by subtracting the current time from the total expected time the boost should run
  - For example, if the profile is loaded at 01:35:00, the expected time left in the boost would be 25 minutes
- If the boost is running 
  - If the "Use Consumables if already running" option is not selected, the consumable will not be used
  - If the time left on the boost is within 1 minute of the expected end time or later, the consumable will not be used
    - For example if there are 24 minutes or more left in the boost and the expected amount of time left is 25 minutes, no lucky charms will be used
- If we make it to this point, check to see how many consumables should be used (if any)
  - Get the total amount of time we need to add to the consumable timer by subtracting the time left (if any) from the total expected time left that the boost should be running
    - For example if the profile is loaded at 01:35:00 and there are 0 minutes left in the boost time, the amount of time we'd need to add is 25 minutes (so the lucky charm expires at 02:00:00). If there is 15 minutes left in the boost time we'd need to add 10 minutes.
  - Get the number of consumables to use by dividing the amount of time to add by the amount of time each consumable adds to the timer and rounding the result to get as close to the expiration as possible
    - For example if we need to add 25 minutes to the boost time, we'd consume 1 of the 2 lucky charms: 25/30 = .833 => 1. If we need to add 10 minutes to the boost time we'd consume 0 of the 2 lucky charms: 10/30 = .333 => 0

Beta potions and MacGuffin Muffins only activate if not already active. MacGuffin Muffin is a special case in that it lasts until the next rebirth AND for 24 hours, whichever is longer. If MacGuffin Muffin has gone through a rebirth, it will no longer be "Active" and will use the same timing logic as other non-beta consumables.

There is **NO** consideration for the possibility that a consumable was used manually, two of the same consumable being in the same breakpoint, multiple breakpoints overlapping, or the fact that alpha and delta potions share a timer. Injector will evaluate whether to use each consumable independant of any other configured consumables or external consumable use.

Be mindful of these limitations or your AP will be sad. No one likes sad AP.

# Zone Stat Overrides

The optimal zone for gold sniping is calculated using a set of values from pins that show stats necessary to do each zone. If the manual threshold is met for a zone, the script will snipe a boss without fast combat. If the idle threshold is met, the script will snipe a boss using fast combat. Beast Mode will always be turned off for this.

The stats for the zones can be manually overriden using the `zoneOverrides.json` file in the user's directory. For the default stats used see [here](https://github.com/rvazarkar/NGUAdvisor/wiki/Default-Zone-Stats-for-Sniping)

# Hotkeys

| Key | Action |
|---|---|
| **F1** | Open / focus the NGU Advisor window. |
| **F2** | Pause / resume all automation (global on-off). |
| **F3** | Quicksave — writes `NGUSave.txt` and `NGUSave.json` to the AppData folder (the JSON loads into Gear Optimizer). |
| **F5** | Dump currently-equipped gear IDs to the log — handy for building loadouts and gear breakpoints. |
| **F7** | Quickload — load the save made by F3. |
| **F8** | Toggle your quick loadout / diggers / beards swap on and off. |
| **F9** | Open the Profile Editor. |

# Acknowledgements

This repository is a fork of [Glowey-Glow/NGUAdvisor](https://github.com/Glowey-Glow/NGUAdvisor), which is itself a fork in the lineage of **NGUInjector** by [rvazarkar](https://github.com/rvazarkar/NGUInjector) and later work by [rus9384](https://github.com/rus9384). Licensed under Apache 2.0 — see [LICENSE](LICENSE).

- [SharpMonoInjector](https://github.com/warbler/SharpMonoInjector) — the injection this is built on. None of this would be possible without it.
- 4G for making NGU Idle.
- JShepler on Discord for the GO bookmarklet and help with code.
