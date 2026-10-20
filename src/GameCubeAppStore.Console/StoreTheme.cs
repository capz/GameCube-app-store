using Dolphin.Graphics;

namespace GameCubeAppStore.Console;

/// <summary>Reusable PSO Episode III-inspired flat panel and palette primitives.</summary>
internal static class StoreTheme
{
    internal static readonly Color32 Background = new Color32(11, 36, 47);
    internal static readonly Color32 Header = new Color32(32, 72, 82);
    internal static readonly Color32 Sidebar = new Color32(25, 58, 68);
    internal static readonly Color32 Surface = new Color32(22, 54, 65);
    internal static readonly Color32 Card = new Color32(37, 74, 86);
    internal static readonly Color32 Border = new Color32(119, 166, 172);
    internal static readonly Color32 Pale = new Color32(181, 211, 215);
    internal static readonly Color32 Text = new Color32(229, 248, 249);
    internal static readonly Color32 Muted = new Color32(158, 194, 201);
    internal static readonly Color32 Accent = new Color32(183, 91, 53);
    internal static readonly Color32 Orange = new Color32(254, 168, 73);

    internal static void Panel(Draw2D draw,float x,float y,float width,float height,
        float corner,Color32 fill,Color32? outline = null)
    {
        var border = outline ?? Border;
        CutRectangle(draw,x,y,width,height,corner,border);
        CutRectangle(draw,x+2,y+2,width-4,height-4,corner-1,fill);
    }

    private static void CutRectangle(Draw2D draw,float x,float y,float w,float h,float c,Color32 color)
    {
        draw.FillRectangle(new RectF(x+c,y,w-2*c,h),color);
        draw.FillRectangle(new RectF(x,y+c,w,h-2*c),color);
        draw.FillTriangle(new Vector2(x+c,y),new Vector2(x,y+c),new Vector2(x+c,y+c),color);
        draw.FillTriangle(new Vector2(x+w-c,y),new Vector2(x+w,y+c),new Vector2(x+w-c,y+c),color);
        draw.FillTriangle(new Vector2(x,y+h-c),new Vector2(x+c,y+h),new Vector2(x+c,y+h-c),color);
        draw.FillTriangle(new Vector2(x+w,y+h-c),new Vector2(x+w-c,y+h),new Vector2(x+w-c,y+h-c),color);
    }

    internal static void Placeholder(Draw2D draw,float x,float y,float w,float h)
    {
        draw.FillRectangle(new RectF(x,y,w,h),new Color32(40,91,101));
        draw.FillRectangle(new RectF(x+w*.28f,y+h*.20f,w*.16f,h*.16f),new Color32(170,216,213));
        draw.FillTriangle(new Vector2(x,y+h),new Vector2(x+w*.49f,y+h*.25f),
            new Vector2(x+w*.73f,y+h),new Color32(55,116,125));
        draw.FillTriangle(new Vector2(x+w*.27f,y+h),new Vector2(x+w*.42f,y+h*.23f),
            new Vector2(x+w*.72f,y+h),new Color32(20,57,70));
    }
}
