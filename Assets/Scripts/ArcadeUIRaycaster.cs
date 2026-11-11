using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// ArcadeCamera のように低い解像度で描いているカメラの UI でも、マウスが正しく当たるようにする。
    /// 画面の上のマウスの位置（例：横1920）を、カメラが描いている板の大きさ（例：横977）に直してから判定する。
    /// ふつうは ArcadeUICanvas が自動で付けるので、手で付けなくてよい。
    /// </summary>
    public class ArcadeUIRaycaster : GraphicRaycaster
    {
        public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
        {
            Camera cam = eventCamera;
            RenderTexture rt = cam != null ? cam.targetTexture : null;
            if (rt == null || Screen.width <= 0 || Screen.height <= 0)
            {
                base.Raycast(eventData, resultAppendList);
                return;
            }

            Vector2 original = eventData.position;
            eventData.position = new Vector2(original.x * rt.width / Screen.width, original.y * rt.height / Screen.height);
            try
            {
                base.Raycast(eventData, resultAppendList);
            }
            finally
            {
                eventData.position = original;
            }
        }
    }
}
