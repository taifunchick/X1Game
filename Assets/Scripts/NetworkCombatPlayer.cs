using Mirror;
using UnityEngine;
using System.Collections;

[System.Serializable] public struct WeaponDamage { public string id; public float damage; }
/// Add to the player prefab. Only the server changes health, kills and respawns.
public class NetworkCombatPlayer : NetworkBehaviour
{
    [SyncVar] public float health = 100f;
    [SyncVar] public string team = "";
    [SerializeField] public WeaponDamage[] weapons = { new WeaponDamage { id="Rifle", damage=25 }, new WeaponDamage { id="Shotgun", damage=60 } };
    [SerializeField] float range = 200f; [SerializeField] LayerMask hitMask = ~0;
    [SerializeField] Transform fireOrigin;
    [SyncVar] public int kills;
    bool dead;
    void Update() { if (!isLocalPlayer || dead || (NetworkRoundManager.Instance && NetworkRoundManager.Instance.finished)) return; if (Input.GetMouseButtonDown(0)) CmdFire(Camera.main ? Camera.main.transform.position : transform.position + Vector3.up, Camera.main ? Camera.main.transform.forward : transform.forward, 0); }
    [Command] void CmdFire(Vector3 origin, Vector3 direction, int weaponIndex) {
        if (dead) return; float damage = (weaponIndex >= 0 && weaponIndex < weapons.Length) ? weapons[weaponIndex].damage : 25f;
        if (Physics.Raycast(origin, direction, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore)) { var target = hit.collider.GetComponentInParent<NetworkCombatPlayer>(); if (target != null && target != this && target.team != team) target.ServerTakeDamage(damage, this); }
    }
    [Server] void ServerTakeDamage(float damage, NetworkCombatPlayer attacker) { if (dead) return; health = Mathf.Max(0, health - damage); if (health <= 0) { dead = true; if (attacker) attacker.kills++; StartCoroutine(Respawn()); } }
    [Server] IEnumerator Respawn() { yield return new WaitForSeconds(3); Transform[] points = FindObjectsOfType<Transform>(); var valid = new System.Collections.Generic.List<Transform>(); foreach (var p in points) if (p.CompareTag("Respawn")) valid.Add(p); if (valid.Count > 0) transform.SetPositionAndRotation(valid[Random.Range(0, valid.Count)].position, transform.rotation); health = 100; dead = false; }
    public override void OnStartLocalPlayer() { base.OnStartLocalPlayer(); if (fireOrigin == null) fireOrigin = transform; }
}
