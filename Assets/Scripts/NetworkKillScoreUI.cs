using Mirror; using UnityEngine; using TMPro;
/// Add to a scene network object; assign the two TMP fields.
public class NetworkKillScoreUI : NetworkBehaviour {
 [SerializeField] TMP_Text redText, blueText;
 void Update(){ int r=0,b=0; foreach(var p in FindObjectsOfType<NetworkCombatPlayer>()){ if(p.team=="Red") r+=p.kills; else if(p.team=="Blue") b+=p.kills; } if(redText)redText.text=r.ToString(); if(blueText)blueText.text=b.ToString(); }
}
