# Three-Round Campaign Prototype

The campaign starts automatically whenever a scene containing `RoundManager` is
played. `Map1` is the first enabled build scene.

## Round flow

1. On a new run, choose one free C-tier starter from three different roles.
2. Review the next enemy composition, tactic and buff.
3. Buy characters or the separate modifier offer.
4. Optionally reroll the shop for 1 gold or freeze it for the next round.
5. Purchased characters deploy automatically until all five deployment slots are
   occupied. Further purchases go to the bench, and deployment can still be edited.
6. Lock deployment and choose an initial team tactic in the existing tactic UI.
   Campaign characters keep the role and weapon they were purchased with, so the
   old tactical-role and loadout-assignment pages are skipped completely.
7. Win to receive 5 base gold plus 1 interest per 5 saved gold (maximum 5).

Defeats can be retried and do not award income. Characters are persistent for the
run and respawn at full health because the map scene reloads between rounds.

## Prototype content

- Round 1: one C-tier Rifle Defender, deployment limit 5, basic hold tactic.
- Round 2: two C-tier enemies, deployment limit 5, split defence and +5% stats.
- Round 3: three mixed C/B-tier enemies, deployment limit 5, aggressive rotations
  and a stronger stat buff.

Character tiers affect health, damage, accuracy, movement and role abilities. The
shop exposes C-tier characters in rounds 1-2; round 3 can offer B-tier characters
and has a small A-tier chance so all tier data can be exercised.

## Current modifiers

- Squad Vitality: permanent +5% player health; repeat purchases stack.
- Sharpened Weapons: permanent +5% player damage; repeat purchases stack.
- Opening Haste: next battle, +20% player movement for 10 seconds.
- Disruption Field: next battle, -20% enemy movement for 10 seconds.

The current implementation reuses the five red and five blue scene agents as
deployment slots. It does not require scene prefab changes. Round content lives in
`CampaignRoundDefinition.CreatePrototypeRounds()` and can later be migrated to
ScriptableObject assets when the prototype balance is settled.

Campaign enemies are always revealed visually; the normal team-vision hiding rules
remain available outside campaign play.
