"""Source-asset checks; run with python3 -m unittest discover -s tests -v.

These do not replace importing/building the project in Unity.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'Assets'
PLAYER = ASSETS / 'StarterAssets/ThirdPersonController/Prefabs/PlayerArmature.prefab'


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


if __name__ == '__main__':
    unittest.main()
