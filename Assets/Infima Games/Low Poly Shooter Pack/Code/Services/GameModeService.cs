//Copyright 2022, Infima Games. All Rights Reserved.

using Mirror;
using UnityEngine;

namespace InfimaGames.LowPolyShooterPack
{
    /// <summary>
    /// Game Mode Service.
    /// </summary>
    public class GameModeService : IGameModeService
    {
        #region FIELDS
        
        /// <summary>
        /// The Player Character.
        /// </summary>
        private CharacterBehaviour playerCharacter;
        
        #endregion
        
        #region FUNCTIONS
        
        public CharacterBehaviour GetPlayerCharacter()
        {
            CharacterBehaviour local = FindLocalCharacter();
            if (local != null)
            {
                playerCharacter = local;
                return playerCharacter;
            }

            if (playerCharacter != null)
                return playerCharacter;

            playerCharacter = Object.FindObjectOfType<CharacterBehaviour>();
            return playerCharacter;
        }

        static CharacterBehaviour FindLocalCharacter()
        {
            foreach (var character in Object.FindObjectsOfType<CharacterBehaviour>())
            {
                var identity = character.GetComponent<NetworkIdentity>();
                if (identity != null && identity.isLocalPlayer)
                    return character;
            }

            return null;
        }
        
        #endregion
    }
}
