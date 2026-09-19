WEB DIR x1team.ru 
/var/www/www-root/data/www/dev.x1team.ru/wp-content/plugins/x1_unity/games 

Builds - ./Builds_!gitignore

## WebGL-билд: что уже вырезано, чтобы он не висел

* `com.unity.probuilder` убран из `Packages/manifest.json`. Его шейдер-граф
  `ProBuilder/Standard Vertex Color` готовил 18 119 вариантов и вешал сборку на
  `preparing variants`; ProBuilder в проекте не используется (0 ссылок из сцен/префабов/кода).
* `com.unity.visualscripting` и `com.unity.multiplayer.center` убраны (0 ссылок).
* `Assets/Mirror/Examples` переименован в `Assets/Mirror/Examples~`: папку с `~` Unity
  не импортирует, `Mirror.Examples.dll` больше не линкуется в плеер (20 МБ и ~1300 файлов из импорта).
* Burst для WebGL отключён (`ProjectSettings/BurstAotSettings_WebGL.json`), неиспользуемые
  шейдеры и `Examples & Extras` TextMesh Pro вырезаны.

## Если билд снова встал на «preparing variants / compiling shader variants»

1. Посмотреть, какой именно шейдер: `Edit > Project Settings > Graphics > Additional Shader
   Stripping Settings` → `Export Shader Variants`, затем `Temp/shader-stripping.json`
   (там число вариантов на шейдер).
2. В том же окне, блок Shader Stripping, выставить Automatic/Strip Unused:
   Lightmap Modes = Automatic, Fog Modes = Automatic, Instancing Variants = Strip Unused,
   BatchRendererGroup Variants = Strip all (в трёх билд-сценах нет ни одного лайтмапа).
3. Разово удалить `Library/ShaderCache` — после удаления пакетов кэш вариантов протухает,
   и первый билд всё равно будет дольше обычного.
4. `Project Settings > Player > WebGL > Optimization`: Managed Stripping сейчас Minimal
   (отсюда 14 МБ wasm). High даёт заметный минус по размеру, но для Mirror тогда нужен
   `link.xml` — делать только если размер важнее времени.
5. Никогда не класть ничего в папки с именем `Resources` — их содержимое Unity берёт в
   билд без вопросов, независимо от сцен билда.

## Мусор в репозитории

`mono_crash.*.blob` (дампы крашей редактора, 3 x 10 МБ) удалены из гита и добавлены в
`.gitignore`. `Builds_!gitignore/` (201 МБ, включая `web.zip`/`sv.zip`) по-прежнему
версионизируется намеренно — если это не нужно, скажите, вынесу в `.gitignore` + Git LFS.

