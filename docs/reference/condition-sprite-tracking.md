> Upstream FM26 Access reference, release 0.2.16. Check [FM27 migration notes](../fm27-migration.md) before using game-specific APIs.

# Condition and Morale Sprite Tracking

Cross-referencing sprite names from the squad overview player cards with the text labels shown in each player's dropdown popup. Condition changes during a match, so sprite values shift over time.

## Condition (heart icon) — CONFIRMED

Sprite format: `level=N, size=small 16px_2x`

| Sprite Value | Condition Label |
|-------------|-----------------|
| level=1     | Terrible        |
| level=2     | Poor            |
| level=3     | Fair            |
| level=4     | Good            |
| level=5     | Excellent       |

Level 1 has not been observed in-game yet. Level 5 maps to both "Excellent" and "Peak" in the game's own text, but we announce it as "Excellent".

## Morale (face icon) — IN PROGRESS

Sprite format: `happiness=X, size=small 16px, filled=false_2x`

The game text labels (e.g. "Focused", "Pleased", "Composed") are irrelevant to this mapping. Multiple text labels share the same sprite, so the text is not useful. We are mapping sprite values to visual appearance only, then choosing an announcement label that describes what the icon looks like.

| Sprite Value      | Visual Appearance | Announced As |
|-------------------|-------------------|--------------|
| happiness=1       | frown / unhappy face | Unhappy |
| happiness=2       | slight frown | (TBD) |
| happiness=3       | (not yet observed) |             |
| happiness=4       | slight smile | (TBD) |
| happiness=5       | full smile / happy | (TBD) |
| happiness=neutral | straight-mouthed / expressionless | Neutral |

### Current Understanding

The morale sprite is a face icon next to each player's match rating. It uses a scale from 1 to 5, plus a "neutral" value. Higher numbers = happier faces.

**Confirmed sprites:**
- `happiness=neutral` — straight mouth, no expression. Currently the only positive-or-above sprite mapped in the code (announced as "Neutral morale").
- `happiness=4` — slight smile, one step above neutral. Common when winning comfortably.
- `happiness=5` — full, wide smile. The happiest. Observed when the whole squad's morale rose during a convincing win.
- `happiness=1` — documented as a frown/unhappy face and mapped in the code as "Unhappy", but never actually observed in-game.

**Unobserved sprites:**
- `happiness=2` and `happiness=3` — never seen. Presumably somewhere between frown and neutral.

**Leading theory:** `happiness=3` doesn't exist — `happiness=neutral` replaces it as the middle value. The numbered scale only covers the extremes: 1-2 for unhappy, 4-5 for happy. This means the full scale is: 1 (worst possible morale), 2 (slightly unhappy), neutral (middle), 4 (slightly happy), 5 (happiest).

**Code status:** Only `happiness=neutral` and `happiness=1` are mapped in the code. When a player has `happiness=4` or `5`, their morale is silently dropped from the announcement (logged as a warning). Once we're confident in labels for 4 and 5, they need to be added to the code.

### Observations

- **Match 1 (winning 2-0, ~59 min):** Rowntree (sub, ST) showed `happiness=2` (slight frown) with a 6.6 rating despite the team winning. He had been `happiness=neutral` earlier when first subbed on.
- **Match 1 (winning 2-0, ~39 min):** Most players had `happiness=4` (slight smile), a few had `happiness=neutral`. By halftime (~45 min), the whole squad shifted to `happiness=5` (full smile) except sub Rowntree who stayed `happiness=neutral`.
- `happiness=4` is visibly distinct from `happiness=5` — the smile is smaller/subtler.
- `happiness=neutral` and `happiness=5` are easy to distinguish: straight mouth vs full smile.

### Next Steps

- Need negative in-game events (conceding goals, red cards, losing streaks) to trigger `happiness=1` and `happiness=3`.
- Method: navigate through all 11 player cards during a match — the mod logs each sprite value automatically. Then take a screenshot for visual cross-reference.
- Once all sprites are observed and labelled, add the mappings to the code so morale is announced for every player.
