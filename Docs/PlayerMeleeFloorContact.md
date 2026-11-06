# Player melee floor contact

## Scope and cause

The authored arcs use `Half Height = 0`, meaning no additional thickness limit. With the corrected 1.8 m range including the ground below the 1.758 m standing camera, a large floor collider enters the spherical candidate query. Without the arc-plane height check, its surface can pass the lateral-angle filter even while looking horizontally. The impact bridge then correctly emits its Hit reaction for that accepted contact.

## Fix and validation plan

1. Preserve the current Player prefab, combat source, tests and live scene.
2. Reproduce the horizontal floor reaction with the actual prefab attack configurations and real `ImpactReactionSignalBridge`/`ReactionSignalReceiver`.
3. Set the authored arc half height to 0.18 m (0.36 m total thickness), measured around the aimed arc plane. Keep explicit zero as an unlimited-height option, with a clear tooltip warning.
4. Confirm horizontal/upward attacks do not trigger floor reactions, horizontal targets still receive hits, and downward attacks still trigger floor impacts on all three combo steps.

Checkpoint: `.codex/backups/player-melee-floor-contact-20261105/`.

## Result

The three authored Player combo steps now use **Half Height = 0.18 m**, a 0.36 m total contact band around the aimed arc plane. New AttackConfig instances use the same default. The 1.8 m range, lateral angles, damage and timing are retained. Explicit zero still means no additional height restriction, and its tooltip explains that nearby floors may then receive hits even when aiming horizontally.

The floor's reaction setup and the production impact bridge were not suppressed or modified: they still respond to genuine accepted contacts. The fix bounds the attack region before damage/impact dispatch.

## Reproduction and tests

The new regression test used the authored Player attack configs and a large floor collider with the real `ImpactReactionSignalBridge` and `ReactionSignalReceiver` Hit event. Before the fix, facing forward produced **three floor reactions**. After assigning finite height, the horizontal combo produced no floor reactions while a forward target still received all three hits. Downward aim still produced three floor reactions and upward aim produced none.

All **16 focused Editor tests passed** after the fix. The suite retains an explicit unlimited-height reproduction case to document zero semantics, plus the existing camera-angle, range, height, deduplication and Ball/Box regression checks.

Baseline evidence: `.codex/diagnostics/player-melee-floor-contact-reproduction-20261105.txt`. Final evidence: `.codex/diagnostics/player-melee-floor-contact-20261105.txt`.

## Native Play Mode validation

The actual Player prefab ran against a large floor collider with the real impact bridge and receiver. The horizontal three-hit combo damaged the forward target three times and produced **zero floor damage/reactions**. Looking down produced three floor impacts and three real Hit reactions, without hitting the forward target again. Looking up hit the upper target and did not increase the floor counters. Disabling vertical aiming preserved horizontal targeting without new floor reactions.

The temporary test scene isolated player input/animation and called the actual hit-window/query methods with controlled camera angles. The original Map3 scene stayed loaded; its unsaved state was retained after Play Mode and its disk file was unchanged. Both builds passed without errors or warnings. Temporary validation scripts/scenes were removed after recording the results.
