# アーケード風の画面効果（ArcadeFX）

ゲーム画面に、参考作品（RACCOIN）のようなアーケード風の効果をかけます。

| 効果 | 内容 |
| --- | --- |
| ドット化 | 3Dの画面を低い解像度で描き、ぼかさずに引き伸ばす |
| 減色とディザ | 色数を減らし、網目模様（ディザ）で中間の色を表す |
| ふちどり | メダルなどの輪郭に、暗い1ドットの線を入れる |
| 走査線 | ブラウン管のような横縞をうっすら入れる |
| 色ずれ | 画面の端ほど、赤と青が少し左右にずれる |
| 周辺減光 | 画面の四隅を暗くする |
| 画面の湾曲 | ブラウン管のふくらみ（初期値はオフ） |

URP でも標準のレンダーパイプラインでも、Unity のバージョンに関係なく動く作りです。

## 入っているファイル

| ファイル | 内容 |
| --- | --- |
| ArcadeCamera.cs | Main Camera に付けるスクリプト。低い解像度で描いて画面に貼る |
| ArcadeScreen.shader | 画面に効果をかけるシェーダー |
| ArcadeScreen.mat | シェーダーを設定済みのマテリアル。値の調整はこれで行う |
| Preview_Compare.png | 効果なし（上）と効果あり（下）の比較 |
| Preview_Zoom.png | 効果ありの拡大 |

## 導入手順

### 1. ファイルとマテリアル

`Assets/ArcadeFX` に入っています。マテリアル（ArcadeScreen.mat）もシェーダーを設定済みなので、作る必要はありません。

### 2. カメラに付ける

1. Hierarchy で **Main Camera** を選ぶ
2. **Add Component** で **Arcade Camera** を追加する
3. **Screen Material** に、`Assets/ArcadeFX/ArcadeScreen.mat` をドラッグする（空のままでも自動で作られるが、ビルドに含めるため設定しておく）
4. 再生する。画面がドット化されていれば成功

効果は再生中だけかかります（編集中のシーンビューにはかかりません）。Inspector の **Effect Enabled** のチェックを外すと、効果なしの画面と見比べられます。

### 3. 光のにじみ（Bloom）を足す（おすすめ）

参考画像の明るいところのにじみは、Unity 標準のポストエフェクトで足します。

このプロジェクトは標準のレンダーパイプラインなので、**Window → Package Manager** で **Post Processing** を入れ、Main Camera に **Post-process Layer** と **Post-process Volume**（Is Global にチェック）を追加して、Volume の Profile に **Bloom** を足します。**Threshold** を 0.9、**Intensity** を 0.6 くらいにします。

ドット化の前にかかるので、にじみもドットになじみます。

## 調整できる値

### ArcadeCamera（Main Camera のコンポーネント）

| 項目 | 初期値 | 内容 |
| --- | --- | --- |
| Pixel Height | 270 | 縦のドット数。小さいほど粗い（180でかなり粗い、360で細かめ） |
| Sorting Order | -100 | ゲーム画面を貼る Canvas の描画順。HUD の Canvas はこれより大きくする |
| Effect Enabled | オン | オフにすると効果を外す |

### ArcadeScreen（マテリアル）

| 項目 | 初期値 | 内容 |
| --- | --- | --- |
| 色の段階数 | 20 | 小さいほど色数が減る。8〜12 にするとレトロ感が強くなる |
| ディザの強さ | 0.6 | 網目模様の強さ。0 で網目なし |
| 彩度 | 1.1 | 色の鮮やかさ |
| ふちどりの濃さ | 0.55 | 0 でふちどりなし |
| ふちどりが出る明るさの差 | 0.16 | 小さいほど、細かい所にも線が出る |
| ふちどりの色 | こげ茶 | 参考画像に合わせた色 |
| 走査線の濃さ | 0.18 | 0 で走査線なし |
| 色ずれの量 | 0.5 | 画面の端での、赤と青のずれ（ドット） |
| 周辺減光の強さ | 0.35 | 四隅の暗さ |
| 画面の湾曲 | 0 | 0.05〜0.1 でブラウン管のようにふくらむ |

## メンバー向けの注意

### 南さん・山本さん（コード）

今の MedalPusher のスクリプトはマウスの位置をボタン代わりに使うだけなので、そのままで問題ありません。今後、マウスの位置から盤面への Ray を作る処理を足す場合だけ、次の点に注意してください。

カメラは RenderTexture に描いているので、**`Camera.ScreenPointToRay` をそのまま使うと、クリックした位置がずれます**。メダルの投入位置など、マウスの位置から Ray を作るときは、ArcadeCamera の関数を使ってください。

```csharp
// 変更前
Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

// 変更後
ArcadeCamera arcade = Camera.main.GetComponent<ArcadeCamera>();
Ray ray = arcade.ScreenPointToRay(Input.mousePosition);
```

### 宮本さん（UI）

- 今のデバッグ用 HUD（DebugHud.cs、OnGUI で描画）は、効果の上に表示されるのでそのまま使えます
- HUD（スコア、ノルマ、ボタンなど）の Canvas は **Screen Space - Overlay** にして、**Sort Order を 0 以上**にしてください。効果がかからず、くっきり表示されます
- HUD もドット風にしたい場合は、ドットフォントや、ドットで描いた画像を使うのがおすすめです（参考画像も、HUD はくっきりしたドットの文字です）

### 稲原さん（演出）

- モニターに World Space の Canvas を置いた場合は、3D の一部として描かれるので、モニターの映像にも効果がかかります
- 効果の強さは演出で変えられます。たとえばボス登場の瞬間に「画面の湾曲」や「色ずれ」を一瞬強くすると、画面が揺らいだような演出になります。マテリアルの値は `material.SetFloat("_ChromaticOffset", 2f)` のように変えられます

| 演出で使える値の名前 | 内容 |
| --- | --- |
| `_ColorLevels` | 色の段階数 |
| `_DitherStrength` | ディザの強さ |
| `_Saturation` | 彩度 |
| `_OutlineStrength` | ふちどりの濃さ |
| `_ScanlineStrength` | 走査線の濃さ |
| `_ChromaticOffset` | 色ずれの量 |
| `_VignetteStrength` | 周辺減光の強さ |
| `_Curvature` | 画面の湾曲 |
