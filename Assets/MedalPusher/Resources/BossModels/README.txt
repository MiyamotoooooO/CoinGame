RECORD DROP ボスの3Dモデル（仮）
==================================

モニターから出てくるボスの手・顔と、お邪魔の小物です。
形は簡単な仮モデルです。動きを確かめるのに使い、あとで Blender で作り直したものに差し替えてください。
OBJ なので、Blender で読み込んで手直しすることもできます。

■ Unity への入れ方
  1. このフォルダごと Assets/Models/Boss などにドラッグする
  2. 色は BossModels.mtl から自動で付く。もし全部白くなったら、OBJ を全部選び、
     Inspector の Materials タブで Material Creation Mode を「Standard」にして Apply
     （色を変えたい時は、同じタブの Extract Materials… でマテリアルを取り出すと編集できる）
  3. 光らせたいマテリアル（下の「光るマテリアル」）は、Emission にチェックを入れて同じ色にする

■ 向きと大きさ（全部共通）
  ・単位はメートル。顔は 0.4〜0.6m、手は 0.25〜0.35m くらい。モニターの大きさに合わせて Scale で調整する
  ・顔は -Z（プレイヤーの方）を向いている。上は +Y
  ・どのモデルも、中の部品（子オブジェクト）が名前付きで分かれている

■ ファイル

  【手と袖（5人共通）】
  Glove_Open.obj   開いた手（ブロックを持つ前、壁を弾く）
  Glove_Grab.obj   つかむ手（ブロック・シャッター・発射口をつかむ）
  Glove_Fist.obj   にぎった手（鎖・掃除機を持つ）
  Glove_Point.obj  指さす手（テレビショッピング、怒る）
    ・原点＝手首。手の甲が上、指先が +Z、右手
    ・左手にする時は Scale X を -1 にする
    ・ポーズを変える時は、4つを切り替える（指を1本ずつ曲げる骨は付いていない）
  Sleeve.obj       袖。原点＝手首（手袋の原点と同じ所）。後ろ（-Z）へ 0.6m 伸びる
    ・Sleeve マテリアルの色をボスごとに変える

  【ボスの顔】
  Boss1_VolumeZero.obj      原点＝つまみの中心
    ・Pointer（針）は原点まわりに Z 回転させると回る。今は「0」を指している
  Boss2_NoiseCanceller.obj  原点＝頭の中心
  Boss3_FaderDown.obj       原点＝頭の中心
    ・Fader_L / Fader_R（目のフェーダー）は Y 方向に動かすと上下する。今は下（DOWN）
    ・Meter_G / Meter_Y / Meter_R（口のメーター）は光らせたり消したりして話す
  Boss4_TempoBreaker.obj    原点＝底の中心
  Boss4_Pendulum.obj        振り子。原点＝回る軸
    ・TempoBreaker の子にして、位置 (0, 0.07, -0.11) に置く。Z 回転で左右に振る
  Boss5_MrMute.obj          原点＝顔の中心。耳の無い白い仮面とシルクハット

  【お邪魔の小物】
  MuteBlock.obj    ミュートブロック（0.2m の箱。4面にスピーカーと×）
  ChainLink.obj    鎖の輪1つ。向きを90度ずつ変えて並べると鎖になる
  Padlock.obj      南京錠（投入ストップ）
  Vacuum.obj       掃除機（吸い口は +Z）
  CraneClaw.obj    クレーンのアーム。原点＝つかむ部分の上
  NoiseMedal.obj   ノイズメダル（灰色）。ゲームのメダルと同じ大きさに Scale で合わせる

■ 光るマテリアル（Emission を入れる）
  Eye_Glow, LED_Orange, Meter_Green, Meter_Yellow, Meter_Red, Lens_Cyan, Mute_Red

■ 袖の色の案
  ボリューム・ゼロ：紺 / ノイズ・キャンセラー：黒 / フェーダー・ダウン：紫 /
  テンポ・ブレイカー：こげ茶 / ミスター・ミュート：白
