using Mirror; 
using UnityEngine;
/// Put on player root and assign movement, input, camera and weapon objects. Prevents remote players from reading local input.
public class LocalPlayerOnly : NetworkBehaviour {
 [SerializeField] Behaviour[] localOnlyBehaviours; [SerializeField] GameObject[] localOnlyObjects;
 public override void OnStartClient(){ bool on=isLocalPlayer; foreach(var b in localOnlyBehaviours) if(b)b.enabled=on; foreach(var g in localOnlyObjects) if(g)g.SetActive(on); }
}
