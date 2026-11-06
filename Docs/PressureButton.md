# Pressure Button

Pressure Button is one type of the [Interaction Trigger System](InteractionTriggerSystem.md).

1. Create **GameObject > Mechanisms > Interaction Trigger System**.
2. Set **Type = Pressure Button**, select the circular prefab and click **Apply Prefab**.
3. Choose **Occupant Filter**: Players, Objects, Enemies, Anything or Specific Object. Configure **Occupancy Mask**, **Detection Height** and **Contact Tolerance** on this controller.
4. Assign **Linked System**, for example `StatuePuzzleController`, or use only the outputs. Configure **Wait for Linked System** and **Interaction Cooldown** here.
5. Open **Edit Prefab (Movement and Audio)**. Configure **Press Distance** (default 0.04 m), **Movement Speed**, **Moving Part**, **Pressure Surface** and the activation/deactivation AudioCues on `InteractionTriggerMechanism`.

Movement and audio belong to the prefab. The system controller owns occupancy and activation policy. Detection uses the prefab's configured travel distance and the original surface pose so the moving plate keeps finding its occupants. Collider deduplication prevents multiple activations per actor/body. Remaining on the plate does not repeat the command; return requires all accepted occupants to leave and the configured target/cooldown wait to finish.

Objects accepts dynamic physics bodies. Static floors/walls do not count in the generic filters. Specific Object accepts colliders on the selected scene object and its descendants, including explicitly chosen static/kinematic objects.

Map3 currently uses Lever as the puzzle activator. To use pressure instead, change Type/Prefab on **Interaction Trigger System**. The puzzle still checks plates and fires the three heads only on failure; cooldown references stay on `StatuePuzzleController.Activation Cooldown Sources`.

The older **Tools > Hazards > Setup Statue Puzzle Pressure Button** menu uses this modular workflow and does not recreate the retired pressure-specific controller.
