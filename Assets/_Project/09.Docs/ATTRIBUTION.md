# 외부 저작물 표시

이 프로젝트가 쓰는 외부 저작물과 그 조건을 적습니다.

라이선스는 "지키면 되는 절차"가 아니라 **배포할 때 따라오는 의무**입니다.
빌드를 남에게 넘기는 순간부터 여기 적힌 문구가 함께 가야 합니다.
그래서 코드 옆이 아니라 문서에 두고, 표시 문구를 그대로 옮겨 쓸 수 있게 적어 둡니다.

---

## Dylearn 3D Pixel Art Grass Demo

- 출처: <https://github.com/DylearnDev/Dylearn-3D-Pixel-Art-Grass-Demo>
- 원저작자: Dylearn

원본은 **Godot** 프로젝트입니다. 이 프로젝트는 유니티 URP 이므로 파일을 그대로
가져온 것이 아니라, 셰이더 기법을 다시 구현하고 잎 스프라이트만 그대로 씁니다.

### 미술 자산 — CC BY 4.0

| 파일 | 원본 |
| --- | --- |
| `04.Art/01.Images/Resources/Grass/GrassLeaf.png` | `Textures and Materials/grassleaf.png` |
| `04.Art/01.Images/Resources/Grass/AccentLeaf.png` | `Textures and Materials/accentleaf.png` |

- 라이선스: [Creative Commons Attribution 4.0 International](https://creativecommons.org/licenses/by/4.0/)
- **표시 문구**: `Grass assets by Dylearn`
- 변경 사항: 파일 이름을 프로젝트 규약에 맞춰 바꾸었습니다. 그림 자체는 손대지 않았습니다.

CC BY 4.0 은 **변경 여부를 밝힐 것**을 요구합니다. 위의 변경 사항 줄이 그 역할을 합니다.
나중에 잎 그림을 직접 고치면 그 사실을 여기에 덧붙여야 합니다.

> 원본 저장소의 Waterfowl 로고는 저작자가 권리를 유보했습니다. 가져오지 않았습니다.

### 셰이더 기법 — MIT

`SRPG_Grass.shader` 와 `SRPG_Toon.hlsl` 의 다음 기법은 원본 `Shaders/Grass.gdshader`
및 `Shaders/clouds.gdshaderinc` 에서 왔습니다.

- 양자화 프레임레이트 (`quantised framerate`)
- 회전 흔들림과 가짜 원근 (`world/view space sway`, `fake perspective`)
- 발산각을 준 두 겹 노이즈 (`noise_diverge_angle`)
- 인물 기반 눌림 회전 (`character displacement`)
- 하이브리드 툰 명암 (`threshold_gradient_size`)

원본 코드의 라이선스는 MIT 입니다. MIT 는 저작권 표시와 라이선스 전문을 함께
남길 것을 요구하므로, 아래에 전문을 옮깁니다.

```
MIT License

Copyright (c) Dylearn

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## 빌드에 넣어야 하는 문구

크레딧 화면이나 `README` 에 아래 한 줄이 반드시 들어가야 합니다.

```
Grass assets by Dylearn — https://github.com/DylearnDev/Dylearn-3D-Pixel-Art-Grass-Demo (CC BY 4.0)
```

---

## unity-isometric-pixel-pipeline

- 출처: <https://github.com/bababuyyy/unity-isometric-pixel-pipeline>
- 라이선스: MIT

이 프로젝트의 픽셀아트 렌더 파이프라인이 이 저장소의 구조를 근거로 세워졌습니다.
파일을 그대로 가져오지는 않았고, 기법과 그 이유를 읽고 이 프로젝트의 구조에 맞춰 다시 구현했습니다.

### 가져온 기법

| 기법 | 우리 쪽 자리 |
| --- | --- |
| 외곽선을 내부 해상도에서 검출해 선을 1픽셀로 고정 | `SRPG_PixelOutline.shader` · `PixelArtFeature` |
| 실루엣(깊이)과 크리스(노멀)를 나누어 검출 | `SRPG_PixelOutline.shader` |
| 카메라를 텍셀 격자에 붙이고 나머지를 UV 로 되돌리는 픽셀 정렬 패닝 | `PixelSnapCamera` · 확대 패스의 `_PixelPanOffset` |
| GPU 인스턴싱 풀에 SSAO 를 끄는 요구 | `BattleWiring` ⑨ |

전투 카메라를 원근에서 직교로 옮긴 것도 이 저장소의 판단을 따른 것입니다.
텍셀이 덮는 월드 길이가 화면 전체에서 같아야 격자를 붙잡을 수 있고, 그것은 직교에서만 성립합니다.

### 원저작자가 밝힌 계보

저 저장소의 문서가 자신의 출처로 밝힌 사람들입니다. 기법의 계보가 여기서 이어집니다.

- **t3ssel8r** — 원안과 외곽선 접근
- **David Holland** — 픽셀 정렬 패닝
- **KodyKing** — 크리스 검출
- **Roystan** — Roberts Cross 외곽선
- **keijiro** — 후처리

### 라이선스 전문

```
MIT License

Copyright (c) 2026 bababuyyy

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

> MIT 는 저작권 표시와 위 전문을 <b>함께 배포할 것</b>을 요구합니다.
> 저작권 연도와 표기는 저장소의 `LICENSE` 파일을 확인해 실제 문구로 맞추십시오 —
> 위 전문은 MIT 표준형이며, 연도·이름이 다르면 그쪽을 따릅니다.

---

## Unity First Person Melee — 검 타격음

- 출처: <https://github.com/BigAndCrispy/Unity-First-Person-Melee>
- 라이선스: **CC0 1.0 Universal** (퍼블릭 도메인 헌정)

| 파일 | 원본 |
| --- | --- |
| `06.Sound/SFX/SFX_Slash.mp3` | `Assets/Audio/Sword_Hit.mp3` |

- 변경 사항: **파일 이름을 배선 규약에 맞춰 바꾸었습니다.** 소리 자체는 손대지 않았습니다.

`BattleAudio_Default` 뱅크의 `Slash` 칸에 들어가 검이 벨 때 나던 합성음을 대신합니다.

**연결은 손이 아니라 도구가 합니다** — `SRPG → 배선 → ⑪`. 뱅크는 구워지는 에셋이라
손으로 꽂아 두면 다시 구울 때 조용히 사라지고, 그 증상은 "어느 날부터 소리가 안 난다"로만
나타납니다. 그래서 파일 이름(`SFX_<칸 이름>`)이 곧 배선이고, 이름을 바꾼 이유가 그것입니다.

CC0 는 표기 의무가 없습니다. 그래도 적는 이유는 이 문서의 다른 항목과 같습니다 —
**어느 자산이 어디서 왔는지 나중에 되짚을 수 있게** 하기 위해서입니다.

### 왜 이것을 골랐는가

저장소 README 가 사운드를 **저작자 본인이 만들었다**고 밝히고 있고(`made by me`),
`LICENSE` 에 CC0 1.0 전문이 들어 있으며, **파일이 저장소 안에 있습니다.**
셋이 함께 성립하는 것이 이 프로젝트가 자산을 들이는 최소 조건입니다.

---

## Kenney Asset Pack — RPG 사운드

- 출처: <https://github.com/iwenzhou/kenney> (`Audio (295 files)/RPG sounds (50 sounds)/`)
- 원저작자: **Kenney Vleugels** (<https://kenney.nl>)
- 라이선스: **CC0 1.0 Universal** (저장소 `LICENSE.md` 에 전문)

| 파일 | 원본 | 들어간 칸 |
| --- | --- | --- |
| `06.Sound/SFX/SFX_Pierce.ogg` | `knifeSlice2.ogg` | 자돌 — 창·화살 |
| `06.Sound/SFX/SFX_Blunt.ogg` | `chop.ogg` | 타격 — 둔기 |
| `06.Sound/SFX/SFX_Death.ogg` | `dropLeather.ogg` | 병사가 쓰러질 때 |

- 변경 사항: **파일 이름을 배선 규약에 맞춰 바꾸었습니다.** 소리 자체는 손대지 않았습니다.

### 왜 이 셋을 골랐는가

이름으로 고른 것입니다. **소리를 직접 듣고 고르지 않았습니다** — 다음에 손볼 때 알아야 할 사실이라 적어 둡니다.

- `knifeSlice2` — 짧고 날카롭게 가르는 소리라 찌르기에 가장 가깝다고 보았습니다
- `chop` — 무겁게 내리치는 소리라 둔기에 맞다고 보았습니다
- `dropLeather` — 가죽 갑옷을 입은 몸이 쓰러지는 소리로 읽었습니다

**맞지 않으면 같은 이름으로 다른 파일을 놓고 ⑪을 다시 돌리면 됩니다.** 배선을 이름 규약으로 둔 것이
그래서입니다 — 소리를 바꾸는 데 코드도 에셋 편집도 필요하지 않습니다.

### `BowRelease` 는 아직 합성음입니다

Kenney 의 RPG·UI·Digital 어느 팩에도 **활 소리가 없습니다.** Digital 폴더에 발사음이 있지만
SF 레이저라 중세 전장에 맞지 않습니다. 맞지 않는 소리를 억지로 넣는 것보다 합성음이 낫다고 보았습니다.

`SRPG → 배선 → ⑪` 이 콘솔에 뱅크 상태를 찍으므로, 이 칸이 비어 있다는 사실은 계속 눈에 보입니다.

### 검토했으나 들이지 않은 것들

| 저장소 | 라이선스 | 들이지 않은 이유 |
| --- | --- | --- |
| [PanderMusubi/sound-effects-library-weapons](https://github.com/PanderMusubi/sound-effects-library-weapons) | CC0 (Still North Media) | 저장소에는 **샘플 두 개뿐**이고 본체는 MediaFire 에 있습니다. 저장소 밖의 파일은 나중에 무엇으로 바뀌었는지 확인할 방법이 없습니다 |
| [JimLynchCodes/Game-Sound-Effects](https://github.com/JimLynchCodes/Game-Sound-Effects) | **없음** | `LICENSE` 파일이 없습니다. 조건을 모르는 자산은 쓸 수 없습니다 |
| [lavenderdotpet/CC0-Public-Domain-Sounds](https://github.com/lavenderdotpet/CC0-Public-Domain-Sounds) | 모음집 | 여러 출처를 모은 것이라 파일마다 원저작자가 다릅니다. 모은 사람이 **자기 것이 아닌 파일까지** 퍼블릭 도메인으로 내놓을 권한은 없습니다 — 위 Roystan 항목에서 이미 같은 판단을 했습니다 |
| [Calinou/kenney-ui-audio](https://github.com/Calinou/kenney-ui-audio) | CC0 (Kenney Vleugels) | **조건은 깨끗합니다.** 다만 UI 클릭·스위치 50종이라 전투에 쓸 것이 없습니다. 승급 화면 같은 UI 소리가 필요해지면 이쪽이 첫 후보입니다 |

---

## 참고한 셰이더 — 값과 기법

파일을 가져오지 않았고 텍스처도 쓰지 않았습니다. **수치와 기법만 참고**했으며,
어느 값이 어디서 왔는지 남겨 두기 위해 적습니다. 셋 다 상업적 사용이 가능한 라이선스입니다.

| 저장소 | 라이선스 | 참고한 것 |
| --- | --- | --- |
| [danielshervheim/unity-stylized-water](https://github.com/danielshervheim/unity-stylized-water) | BSD-3-Clause | 물가 거품의 **세기를 폭과 따로 두는 구조**(`_FoamContribution` ≈ 0.55). 우리 `_ShoreStrength` 의 기본값이 여기서 왔습니다 |
| [MatrixRex/Uber-Stylized-Water](https://github.com/MatrixRex/Uber-Stylized-Water) | MIT | 프리셋을 성격별로 나누는 구성 방식 |
| [bababuyyy/unity-isometric-pixel-pipeline](https://github.com/bababuyyy/unity-isometric-pixel-pipeline) | MIT | 위의 항목 참조 |

> 텍스처를 가져오지 않은 이유를 함께 적어 둡니다.
> 저 저장소들이 함께 배포하는 물 노멀맵·거품 텍스처는 사실적 표현을 전제로 만들어졌습니다.
> 이 프로젝트는 내부 해상도 270~540 에 툰 밴딩을 걸므로 그 결이 화면에 남지 않습니다.
> 그리고 텍스처는 그 자체의 출처가 저장소 문서에 밝혀져 있지 않아, 라이선스가 코드와 같다고
> 단정할 수 없습니다. **확인되지 않은 자산은 들이지 않습니다.**

---

## Toon Water Shader (Roystan)

- 출처: <https://github.com/IronWarrior/ToonWaterShader>
- 원저작자: Erik Roystan Ross
- 라이선스: **Unlicense** (퍼블릭 도메인 헌정)

Unlicense 는 표기 의무조차 없습니다. 그래도 적어 두는 이유는 의무가 아니라
**어느 판단이 어디서 왔는지 나중에 되짚을 수 있게** 하기 위해서입니다.

### 가져온 기법

물가 거품을 <b>문턱</b>으로 만드는 방식입니다.

```
surfaceNoiseCutoff = foamDepthDifference01 * _SurfaceNoiseCutoff;
surfaceNoise = smoothstep(cutoff - AA, cutoff + AA, noiseSample);
```

얕을수록 문턱을 낮추고, 노이즈가 그 문턱을 넘었는지만 봅니다.
우리 `SRPG_Water.shader` 의 `_ShoreCutoff` · `_ShoreCutoffAA` 가 여기서 왔습니다.

예전에는 노이즈를 깊이에 더한 뒤 `pow` 로 떨어뜨렸는데, 그러면 값이 매끄럽게 이어져
에어브러시 자국처럼 보입니다. **만화적인 결은 텍스처가 아니라 이 이분법에서 나옵니다.**

### 가져오지 않은 것 — 텍스처

저장소에는 `PerlinNoise.png` · `WaterDistortion.png` · `Shoreline.png` 이 있습니다.
셋 다 저자 자신의 자산이고 퍼블릭 도메인이지만 들이지 않았습니다.

- **`Shoreline.png`** 은 이름과 달리 거품이 아니라 **데모 씬의 모래톱 오브젝트**가 쓰는 지형 그림입니다.
- **`PerlinNoise.png`** 은 우리 절차적 `Fbm` 을 대신할 수 있지만, 지금 거품 규모(`_ShoreNoiseScale` 0.55)에서는
  텍스처가 약 1.8 월드 단위마다 반복됩니다. 물가를 따라 같은 무늬가 줄줄이 늘어서 오히려 인공적으로 보입니다.
  쓰려면 규모를 함께 다시 잡아야 합니다.
- **`WaterDistortion.png`** 은 가장자리를 흔드는 용도인데, 우리 파도가 이미 도메인 워프
  (`_WaveWarpScale` · `_WaveWarpStrength`)를 갖고 있어 겹칩니다.

> 같은 저장소의 `Wood17_col/disp/nrm/rgh.jpg` (풀 셰이더 저장소 쪽)는 <b>의도적으로 제외</b>했습니다.
> 이름 형식이 PBR 텍스처 배포 사이트의 규칙이고 README 에 출처 표기가 없습니다.
> 저장소가 Unlicense 라도 저자가 <b>자기 것이 아닌 파일까지</b> 퍼블릭 도메인으로 내놓을 권한은 없습니다.
