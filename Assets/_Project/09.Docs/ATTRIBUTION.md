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
