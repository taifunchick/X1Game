"""Source-asset checks; run with python3 -m unittest discover -s tests -v.

These do not replace importing/building the project in Unity.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'Assets'
PLAYER = ASSETS / 'StarterAssets/ThirdPersonController/Prefabs/PlayerArmature.prefab'
LPSP_PLAYER = ASSETS / 'Infima Games/Low Poly Shooter Pack/Prefabs/P_LPSP_FP_CH.prefab'
MIRROR_NETWORK_MANAGER = ASSETS / 'Mirror/Core/NetworkManager.cs'
X1_NETWORK_MANAGER = ASSETS / 'Scripts/X1NetworkManager.cs'
ROBOT_AVATAR = ASSETS / 'Scripts/RobotAvatar.cs'
COMBAT = ASSETS / 'Scripts/NetworkCombatPlayer.cs'


class ServerAssetTests(unittest.TestCase):
    def test_muzzle_flash_keeps_cpu_mesh_data(self):
        meta = ASSETS / 'Infima Games/Low Poly Shooter Pack/Art/Meshes/Effects/SM_MuzzleFlash.FBX.meta'
        self.assertRegex(meta.read_text(), r'(?m)^    isReadable: 1$')

    def test_player_has_no_dangling_component_ids(self):
        text = PLAYER.read_text()
        objects = set(re.findall(r'^--- !u!\d+ &(-?\d+)', text, re.M))
        for component in re.findall(r'component: \{fileID: (-?\d+)\}', text):
            self.assertIn(component, objects)
        self.assertNotIn('RosRobotController', text)
        self.assertNotIn('536ab92ce7431bc498520e9ec9b095e3', text)

    def test_third_person_controller_reference_is_preserved(self):
        meta = ASSETS / 'StarterAssets/ThirdPersonController/Scripts/ThirdPersonController.cs.meta'
        guid = re.search(r'^guid: (\w+)', meta.read_text(), re.M).group(1)
        self.assertIn('m_Script: {fileID: 11500000, guid: ' + guid, PLAYER.read_text())
        self.assertIn('thirdPersonController: {fileID: 4416926081852918490}', PLAYER.read_text())



class DedicatedServerRegressionTests(unittest.TestCase):
    """Дефекты, из-за которых сервер «жив, но не работает» на 2+ игроках."""

    def test_headless_frame_rate_is_limited(self):
        # Без этой строки headless-сервер крутит тысячи кадров в секунду и перестаёт
        # обрабатывать попадания и подключения (процесс при этом не падает).
        self.assertIn('Application.targetFrameRate', MIRROR_NETWORK_MANAGER.read_text())
        self.assertIn('Application.targetFrameRate', X1_NETWORK_MANAGER.read_text())

    def test_shot_uses_single_raycast(self):
        text = COMBAT.read_text()
        self.assertIn('Physics.RaycastNonAlloc', text)
        self.assertNotIn('rayOrigin += direction * step', text)

    def test_lasertag_player_prefab_has_robot_avatar(self):
        prefab = LPSP_PLAYER.read_text()
        script = ROBOT_AVATAR.read_text()
        guid = re.search(r'^guid: (\w+)', (ASSETS / 'Scripts/RobotAvatar.cs.meta').read_text(), re.M).group(1)
        self.assertIn('m_Script: {fileID: 11500000, guid: ' + guid, prefab)
        self.assertIn('class RobotAvatar : NetworkBehaviour', script)
        # Овалы-капсулы больше не показываем вместо соперников.
        self.assertIn('HideLegacyCapsule', script)
        # У робота не должно быть коллайдеров — попадания считает сервер геометрически.
        self.assertIn('Destroy(collider)', script)

    def test_lasertag_player_prefab_components_resolve(self):
        text = LPSP_PLAYER.read_text()
        objects = set(re.findall(r'^--- !u!\d+ &(-?\d+)', text, re.M))
        for component in re.findall(r'component: \{fileID: (-?\d+)\}', text):
            self.assertIn(component, objects)


if __name__ == '__main__':
    unittest.main()
