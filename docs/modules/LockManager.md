# LockManager (`Managers/LockManager.cs`)

The mode-lock state machine: exactly one temporary gear/beard/digger "mode" (Titan, Yggdrasil,
MoneyPit, Gold, Quest, Cooking) may hold the configuration at a time. Each `Try<X>Swap()` is both
the acquire AND the release: it swaps in when the mode's condition holds, and restores when called
again while its own lock is held and the condition has passed.

## Lock rules

- `CanSwap()` = lock is `None` **or `Quest`** — Quest is the only PREEMPTIBLE lock; any other
  mode may take over a quest swap. `_swappedFromQuest` remembers it so the restore hands the lock
  back to Quest (and re-equips quest gear) instead of releasing to None.
- **`currentLock` is explicitly initialized to `None`** — `LockType.Titan` is enum value 0, so an
  uninitialized static would read as a held Titan lock until `Main.Start()` (which still calls
  `ReleaseLock()` as a redundant reset). Keep the initializer on every refactor.
- `SaveConfiguration()` snapshots loadout (skipped when nesting over Quest — the pre-Quest
  snapshot is the real baseline), beards, diggers; `RestoreConfiguration()` reverses them.

## Hard-won invariants (each fixed a real stuck-state bug)

1. **A lock must never outlive an exception.** Every acquire path wraps its body in
   `try/catch { CleanupFailedAcquisition(...); throw; }`. Before this, a throw inside a helper
   (worst: Yggdrasil) stranded the lock for the whole session — and Yggdrasil's restore branch
   sits behind `!NeedsHarvest()`, which the failed harvest would have cleared, so nothing could
   ever release it and `RebirthAvailable()` never came back.
2. **`RestoreConfiguration` transitions the lock inside its own `finally`** — Quest is handed
   back only once the pre-Quest gear is actually re-equipped; any throw releases to None rather
   than stranding the lock.
3. **Beard/digger restores run on EVERY path** (outer `finally`): if they were skipped after the
   lock moved to None, the next `SaveConfiguration()` would snapshot the temporary set as the new
   baseline — permanent loadout corruption.
4. **`RestoreYggdrasilSwap()`** exists because `TryYggdrasilSwap()` cannot serve as the release
   while fruit remains unharvested (exactly what a thrown harvest leaves behind): the call takes
   the acquire branch, fails `CanAcquireNewLock` against its own lock, and restores nothing. The
   dedicated restore is self-selecting — fires only while the Yggdrasil lock is held.
5. In `CleanupFailedAcquisition` the cleanup fault is logged and dropped; the ORIGINAL
   acquisition exception stays authoritative and reaches the caller.

## Mode notes

- **Titan** (`TryTitanSwap`): gold-loadout variant when `ZoneHelpers.ShouldRunGoldLoadout()`
  (gold-kill snipe), else titan kill set via `GearOptimizer.ResolveTitanGear()` (which itself
  overrides loot objectives on real fights — see GearOptimizer.md).
- **Gold** (`TryGoldDropSwap`): on restore with `ManageGoldLoadouts` sets
  `Settings.GoldSnipeComplete = true` — the one-shot gold snipe latch other modules read.
- After `RestoreGear()` the restored set predates the lock and may be stale —
  `AdvisorApply.GearRestored()` re-arms the gear refresh to re-evaluate next tick.
- `GetLockTypeName()` feeds the HUD and equip logs ("Received New Gear for X").
