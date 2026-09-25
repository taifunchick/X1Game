using Mirror; using UnityEngine; using TMPro;
/// Вешается на сетевой объект в сцене; назначить тексты команд (и свой счёт попаданий).
/// Показывает ВСЕГО ПОПАДАНИЙ по командам: каждое попадание в любого игрока
/// даёт стрелку +1 (NetworkCombatPlayer.hits). Убийств больше нет.
public class NetworkKillScoreUI : NetworkBehaviour {
 [SerializeField] TMP_Text redText, blueText;
 [SerializeField] TMP_Text myHitsText;
 int lastRed = -1, lastBlue = -1, lastMy = -1;
 void Update(){
   int r=0, b=0;
   NetworkCombatPlayer me = NetworkClient.localPlayer != null
     ? NetworkClient.localPlayer.GetComponent<NetworkCombatPlayer>() : null;
   int my = me != null ? me.hits : 0;
   var players = NetworkCombatPlayer.Instances;
   for(int i=0;i<players.Count;i++){
     var p=players[i];
     if(p==null) continue;
     if(p.team=="Red") r+=p.hits;
     else if(p.team=="Blue") b+=p.hits;
   }
   if(r==lastRed && b==lastBlue && my==lastMy) return;
   lastRed=r; lastBlue=b; lastMy=my;
   if(redText) redText.text=r.ToString();
   if(blueText) blueText.text=b.ToString();
   if(myHitsText) myHitsText.text="My hits: "+my;
 }
}
