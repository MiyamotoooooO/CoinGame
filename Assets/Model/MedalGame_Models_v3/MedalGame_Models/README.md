# メダルゲーム（仮） 仮モデル一式（プレイヤー視点対応版）

試作フェーズ用の仮モデルです。形・大きさ・動く部品の仕組みを確かめるためのもので、本番の見た目は宮本さんが仕上げる前提です。

参考作品（RACCOIN）のような「筐体の中をのぞき込むプレイヤー視点」に合わせて、盤面を横長にし、左右の壁を斜めに開き、ターンテーブルを左の壁に取り付けています。

- **PlayerView.png**：下の「カメラの設定」で撮った、実際のプレイ画面に近い見え方
- **Preview.png**：プレイヤー視点・全体・ターンテーブル・真上からの4枚

寸法はすべて「メダル直径25mm」を基準にした仮の値です。決まった寸法に合わせて、Source フォルダのスクリプトで作り直せます（いちばん下の「作り直し方」）。

## フォルダの中身

```
MedalGame_Models/
  Models/       … モデル（.obj）とマテリアル定義（.mtl）
  Textures/     … レコードのラベルの型紙
  Source/       … モデルを作ったPythonスクリプト
  PlayerView.png / Preview.png … 確認画像
  README.md     … このファイル
```

## モデル一覧

### 盤面まわり（位置 (0, 0, 0) に置けば組み上がる）

| ファイル | 内容 | 当たり判定 |
| --- | --- | --- |
| Playfield | 盤面、ふちの壁、奥の壁、落下口の受け（床と手前の縁） | Mesh Collider |
| Dividers | 仕切り2枚（中央の穴と端の穴を分ける） | Mesh Collider |
| Pusher | プッシャー（いちばん奥に下がった位置で作ってある） | Mesh Collider（Convex）＋ Rigidbody（Is Kinematic） |
| Hole_Center | 中央の穴の判定ボックス | Box Collider（Is Trigger）。表示はオフ |
| Hole_Side_Right / Hole_Side_Left | 端の穴の判定ボックス（プレイヤーから見て右・左） | Box Collider（Is Trigger）。表示はオフ |
| SideHoleCover_Right / SideHoleCover_Left | 端の穴のふた（レコード「レゲエ」用） | Box Collider。普段は非アクティブ |

### 筐体とモニター（位置 (0, 0, 0) に置く）

| ファイル | 内容 | 当たり判定 |
| --- | --- | --- |
| Cabinet | 土台、外へ開いた左右の斜めの壁、背板、屋根、ターンテーブルの取り付け板 | Mesh Collider |
| MonitorFrame | モニターの枠 | なし |
| MonitorScreen | モニターの画面（横長の板1枚）。稲原さんの演出を映す場所 | なし |

### ターンテーブル（左の壁に取り付ける。回る部品は回転軸が原点）

| ファイル | 内容 | 動き |
| --- | --- | --- |
| Turntable_Base | 本体、スタートボタン、アームの台座 | 動かない |
| Turntable_Platter | 回る皿とスピンドル | Y軸まわりに回す |
| Turntable_Tonearm | トーンアーム | Y軸まわりに40度回すと針がレコードに乗る |

### メダルとレコード

| ファイル | 内容 |
| --- | --- |
| Medal / Medal_Low / Medal_Collider | メダル（詳細版・軽量版・当たり判定用） |
| Record | レコード（A面・B面のラベル、溝の帯） |

## 主な寸法（実寸、mm）

| 部分 | 寸法 |
| --- | --- |
| 盤面 | 幅480 × 奥行き350（前端から奥端まで） |
| ふちの壁 | 盤面から高さ45。その上に斜めの壁が外へ開きながら高さ360まで立ち上がる |
| プッシャー | 幅476 × 奥行き160 × 高さ32。手前へ50動く |
| 落下口 | 中央の穴の幅296、端の穴の幅88（左右それぞれ） |
| モニター画面 | 400 × 160（横長、2.5:1） |
| 筐体 | 幅788 × 高さ810 × 奥行き540 |
| ターンテーブル | 実物大で作り、0.6倍で取り付ける |

## Unityへの取り込み手順

### 1. ファイルを入れる

1. Projectウィンドウで `Assets/Models/MedalGame` フォルダを作る
2. `Models` フォルダの中身と `Textures` フォルダの中身を、そこへドラッグ＆ドロップする

前のバージョンを入れている場合は、同じ名前のファイルを上書きしてください。

### 2. 大きさをそろえる（全モデル共通）

1. Projectウィンドウで .obj をすべて選択する（Ctrlを押しながらクリック）
2. Inspector の **Model** タブで **Scale Factor** を **10** にして **Apply** を押す
3. 続けて **Materials** タブで **Extract Materials...** を押し、マテリアルを書き出す

実寸のメダルは厚さ2.4mmしかなく、Unityの物理（初期設定で接触の余白が10mm）だと不安定になるため、全体を10倍にしています。以下の位置はすべて10倍後の値です。

### 3. 盤面と筐体を組み立てる

1. Hierarchy に空のオブジェクト `MedalMachine` を作り、位置を (0, 0, 0) にする
2. 次のモデルをすべて `MedalMachine` の子にして、位置 (0, 0, 0)、回転 (0, 0, 0) にする
   - Cabinet、Playfield、Dividers、Pusher、MonitorFrame、MonitorScreen
   - Hole_Center、Hole_Side_Right、Hole_Side_Left
   - SideHoleCover_Right、SideHoleCover_Left
3. 当たり判定を付ける（上の表のとおり）
   - Cabinet、Playfield、Dividers：**Add Component → Mesh Collider**
   - Pusher：**Mesh Collider** の **Convex** にチェック、**Rigidbody** を追加して **Is Kinematic** にチェック、**Interpolate** を **Interpolate** にする
   - Hole_ 3つ：**Box Collider** を追加（大きさは自動で合う）し、**Is Trigger** にチェック。**Mesh Renderer** のチェックを外して見えなくする
   - SideHoleCover_ 2つ：**Box Collider** を追加し、オブジェクト自体のチェックを外して非アクティブにする

### 4. ターンテーブルを左の壁に取り付ける

| オブジェクト | 親 | 位置 | 回転 | スケール |
| --- | --- | --- | --- | --- |
| Turntable_Base | MedalMachine | (-2.954, 1.75, 1.25) | (0, 25, -70.8) | (0.6, 0.6, 0.6) |
| Turntable_Platter | Turntable_Base | (0, 0.45, 0) | (0, 0, 0) | (1, 1, 1) |
| Record | Turntable_Platter | (0, 0.1245, 0) | (0, 0, 0) | (0.5, 0.5, 0.5) |
| Turntable_Tonearm | Turntable_Base | (1.05, 0.45, 0.55) | (0, 0, 0)（再生中は (0, 40, 0)） | (1, 1, 1) |

- Turntable_Base の裏側が、Cabinet の取り付け板にぴったり重なれば正しい位置です
- レコードを回す演出では、Turntable_Platter を **ローカルの Y 軸まわり**に回す（`transform.Rotate(0, 速度 * Time.deltaTime, 0, Space.Self)`）。本体ごと傾いているので、ワールドの Y 軸で回さないこと

### 5. カメラの設定（プレイヤー視点）

Main Camera を次の値にします。画面の比率は 16:9 を想定しています。

| 項目 | 値 |
| --- | --- |
| Position | (0, 2.28, -2.62) |
| Rotation | (17.8, 0, 0) |
| Field of View | 56 |
| Clipping Planes の Near | 0.05 |

画面の上に横長のモニター、その下にプッシャーの上段、手前に盤面が広がり、左の壁にターンテーブルが見える構図になります（PlayerView.png）。盤面の手前の端（メダルが落ちる所）は画面の下に隠れます。

### 6. メダルのPrefab

1. Medal_Low を Hierarchy に置く
2. **Mesh Collider** を追加し、Mesh に Medal_Collider を入れて **Convex** にチェック
3. **Rigidbody** を追加する
4. Project ウィンドウへドラッグして Prefab にする

## 南さん・山本さん向けのメモ

- **プッシャーの動かし方**：位置 Z を 0（いちばん奥）と -0.5（いちばん手前）の間で往復させる。Rigidbody の `MovePosition` を FixedUpdate で使う
- **穴の判定**：Hole_ の Trigger に入ったメダルを数える。Hole_Center が中央の穴、Hole_Side_ が端の穴（通常0pt、レコード「フォーク」で+2pt）
- **端の穴のふた**：レコード「レゲエ」の再生中だけ SideHoleCover_ をアクティブにする
- **左右**：「Right」はプレイヤーから見て右（Unity の +X 側）。ターンテーブルはプレイヤーから見て左の壁（-X 側）。もし取り込んだ結果が逆に見えたら教えてください

## 稲原さん向けのメモ

- **MonitorScreen** の画面は横長（2.5:1）。プレイヤーから見て左下が UV (0, 0)、右上が (1, 1)。Render Texture を 1000 × 400 などの同じ比率で作ってマテリアルに貼れば、そのまま映る
- World Space の Canvas を使う場合は、MonitorScreen の位置に重ねて置き、MonitorScreen は非表示にする
- ターンテーブルは左の壁にあり、プレイヤーから常に見えている。レコード再生の演出（回転、アームが下りる動き）はここで見せられる

## 宮本さん向けのメモ

- マテリアルは部分ごとに分けてある（盤面の上面 Field、斜めの壁の内側 CabinetInner、プッシャーの上面 PusherTop など）
- プレイヤー視点で大きく見えるのは、モニター、斜めの壁の内側（CabinetInner）、プッシャーの上段、盤面、ターンテーブル。見た目を作り込むならこの順が効果的
- 筐体の外側（CabinetBody）はプレイヤー視点ではほとんど見えない
- Textures/LabelTemplate.png はレコードのラベルの型紙

## 作り直し方（寸法を変えたいとき）

Python 3 が入っていれば作り直せます（追加のライブラリは不要）。

1. `Source/build_cabinet.py` の上のほうにある「寸法（mm）」の数値を変える（例：`FIELD_W = 480` が盤面の幅、`MON_W, MON_H` がモニター画面の大きさ）
2. Source フォルダでコマンドを実行する
   - 盤面・筐体・ターンテーブル：`python build_cabinet.py 出力先フォルダ`
   - メダル・レコード：`python build.py 出力先フォルダ`
3. できた .obj を Unity の同じファイルに上書きする（Prefab の設定は残る）

ターンテーブルの取り付け位置を変えた場合は、出力先にできる layout.json に新しい位置と角度が書かれます。Unity の値に直すときは、位置は3つとも100で割ってから X の符号だけ反転し、回転は tt_tilt をそのまま X に、tt_yaw と tt_roll は符号を反転して Y と Z に入れると合います。
