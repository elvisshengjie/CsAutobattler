# AI stability and squad-size behavior

Movement uses a 0.75-second minimum commitment for ordinary destination changes. Explicit ForceMoveTo and exact interaction commands remain immediate. Combat holds preserve the route and avoidance state; Stop still cancels movement. Final waypoints use the tactical arrival tolerance, and movement steps are capped to the remaining waypoint distance. Aim direction takes precedence over movement-facing during strafing.

Stuck detection requires net displacement of at least half a unit or one body width. Small oscillation does not count as progress, while movement around corners does. Intermediate recovery waypoints and emergency fallback locations never count as arrival at the original objective. Recovery remains a fallback after sustained blockage.

Combat targets have a short 0.6-second commitment while still detectable. Mid-round commands, target suppression, and priority defuse threats override this commitment.

## Squad tactics

- One or two survivors: feint probes as a group for up to three seconds, then attacks the real site. Split keeps nearby supporting angles rather than separating the pair onto distant routes.
- Three survivors: split 2/1; feint 1 distraction / 2 main.
- Four survivors: initial split 2/2; feint 2 distraction / 2 main.
- Five survivors: split 3/2; feint 2 distraction / 3 main.
- Feints release after eight seconds even without enemy contact or rotation.
- Feint assignments reject unavailable members. Split retains surviving routes while both groups remain populated and rebuilds when a group is lost.
- Feint distractions prefer healthy flankers over support characters. Split keeps the carrier in the main group and favors flankers for the side group.

## Play-mode acceptance checks

Run each initial tactic with 1–5 characters on Map1 and Map2. Include doorways, two characters crossing, crowded cover, loss of the carrier, and loss of an entire split group. Verify that purposeful shooting/holding remains possible, movement resumes after combat, and exact planting/defusing still works. Confirm that deaths release synchronization and that target changes do not cause rapid facing reversals.

Edit-mode regression coverage is in Assets/Tests/Editor/MovementSafetyTests.cs. These tests do not substitute for live-match validation.
