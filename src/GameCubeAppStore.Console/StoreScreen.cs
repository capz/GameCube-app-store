using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Timing;

namespace GameCubeAppStore.Console;

/// <summary>
/// GameCube controller-driven catalog with featured, new and top-rated views.
/// No managed graphics resource is allocated or disposed inside a GX frame.
/// </summary>
internal sealed class StoreScreen : IDisposable
{
    private readonly CatalogClient client;
    private readonly Draw2D draw;
    private readonly SampleText text;
    private readonly BrowserNavigation vertical = new BrowserNavigation();
    private readonly List<CatalogItem> all = new List<CatalogItem>();
    private readonly List<CatalogItem> shown = new List<CatalogItem>();
    private Task<List<CatalogItem>>? catalogTask;
    private Task<CatalogDetailData>? detailTask;
    private Task<byte[]>? pictureTask;
    private Texture2D? picture;
    private string pictureUrl = "";
    private string status = "CONNECTING TO STORE";
    private CatalogDetailData? detail;
    private int menu,selected,scroll,focus;
    private bool viewingDetail;
    private static readonly string[] Categories =
        { "FEATURED", "NEW RELEASES", "TOP RATED", "ALL SOFTWARE" };

    public StoreScreen(CatalogClient client, Draw2D draw, SampleText text)
    {
        this.client = client;
        this.draw = draw;
        this.text = text;
        BeginRefresh();
    }

    private void BeginRefresh()
    {
        catalogTask = client.ListAsync(CancellationToken.None);
        status = "LOADING CATALOG";
    }

    private void CompletedRequests()
    {
        if (catalogTask != null && catalogTask.IsCompleted)
        {
            try
            {
                var result = catalogTask.Result;
                all.Clear();
                all.AddRange(result);
                Rebuild();
                status = all.Count == 0 ? "NO PUBLISHED SOFTWARE YET" : "ONLINE";
            }
            catch(Exception ex) {status = "CATALOG ERROR " + ex.Message;}
            catalogTask = null;
        }
        if (detailTask != null && detailTask.IsCompleted)
        {
            try { detail = detailTask.Result; status = "ONLINE"; }
            catch(Exception ex) { detail = null;status = "DETAIL ERROR " + ex.Message; }
            detailTask = null;
        }
        if (pictureTask != null && pictureTask.IsCompleted)
        {
            try
            {
                var bytes = pictureTask.Result;
                picture?.Dispose();
                picture = Texture2D.FromPng(bytes);
            }
            catch { picture?.Dispose();picture=null; }
            pictureTask = null;
        }
    }

    private CatalogItem? Current => selected>=0&&selected<shown.Count ? shown[selected] : null;

    private void Rebuild()
    {
        shown.Clear();
        for(int i=0;i<all.Count;i++)shown.Add(all[i]);
        if(menu==2||menu==0)
        {
            shown.Sort((a,b)=>
            {
                var ratings=b.RatingValue.CompareTo(a.RatingValue);
                return ratings==0?b.RatingCount.CompareTo(a.RatingCount):ratings;
            });
        }
        else if(menu==3) shown.Sort((a,b)=>string.Compare(a.Title,b.Title,StringComparison.OrdinalIgnoreCase));
        // Public API supplies last-updated descending for NEW RELEASES.
        selected=scroll=0;
        detail=null;viewingDetail=false;
        ClearPicture();
        RequestPicture();
    }

    private void ClearPicture()
    {
        picture?.Dispose();
        picture=null;
        pictureUrl="";
    }

    private void RequestPicture()
    {
        if(pictureTask!=null)return; // wait until previous request completes
        string url = viewingDetail && detail!=null && detail.Screenshots.Count>0
            ? detail.Screenshots[detail.ImageIndex] : Current?.CoverUrl ?? "";
        if(url.Length==0) {ClearPicture();return;}
        if(url==pictureUrl)return;
        ClearPicture();
        pictureUrl=url;
        pictureTask=client.GetScreenshotAsync(url,CancellationToken.None);
    }

    private void MoveSelection(int change)
    {
        if(focus==0)
        {
            menu=(menu+change+Categories.Length)%Categories.Length;
            vertical.Reset();
            Rebuild();
        }
        else if(!viewingDetail && shown.Count>0)
        {
            selected=(selected+change+shown.Count)%shown.Count;
            if(selected<scroll)scroll=selected;
            if(selected>=scroll+3)scroll=selected-2;
            detail=null;
            ClearPicture();
            RequestPicture();
        }
    }

    private void HandleInput()
    {
        Controller.Update();
        var pad=Controller.GetState();
        if(pad.Start.WasPressed){finished=true;return;}
        if(pad.X.WasPressed&&!viewingDetail&&catalogTask==null)BeginRefresh();
        if(pad.B.WasPressed)
        {
            if(viewingDetail)
            {
                viewingDetail=false;detail=null;
                ClearPicture();RequestPicture();
            }
            else focus=0;
            vertical.Reset();
            return;
        }
        if(viewingDetail)
        {
            if(pad.A.WasPressed||pad.Y.WasPressed)
            {
                if(detail!=null&&detail.Screenshots.Count>1)
                {
                    detail.ImageIndex=(detail.ImageIndex+1)%detail.Screenshots.Count;
                    ClearPicture();RequestPicture();
                }
            }
            return;
        }
        int move=vertical.Update(pad.IsConnected,pad.DPadUp.IsHeld,pad.DPadDown.IsHeld,
            pad.Stick.Y,Clock.Timestamp);
        if(move!=0)MoveSelection(move);
        if(pad.DPadLeft.WasPressed){focus=0;vertical.Reset();}
        if(pad.DPadRight.WasPressed){focus=1;vertical.Reset();}
        if(pad.A.WasPressed)
        {
            if(focus==0){focus=1;vertical.Reset();}
            else if(Current!=null)
            {
                viewingDetail=true;detail=null;
                detailTask=client.GetDetailAsync(Current.Id,CancellationToken.None);
                ClearPicture();RequestPicture();
            }
        }
    }

    private bool finished;
    public int Run()
    {
        while(!finished)
        {
            CompletedRequests();
            if(pictureTask==null)RequestPicture();
            HandleInput();
            Render();
        }
        return 0;
    }

    private static string Trim(string? str,int max)
    {
        if(string.IsNullOrEmpty(str))return "";
        return str!.Length>max?str.Substring(0,max):str;
    }

    private void T(string value,float x,float y,float size,Color32 color) =>
        text.Draw(draw,Trim(value,64).ToUpperInvariant(),x,y,size,color);

    private void Render()
    {
        var device=GraphicsDevice.Default;
        device.BeginFrame(StoreTheme.Background);
        draw.Begin();

        StoreTheme.Panel(draw,34,29,892,68,19,StoreTheme.Header);
        T("GAMECUBE / APP STORE",61,48,6,StoreTheme.Text);
        T(Trim(status,28),736,55,2.4f,StoreTheme.Pale);

        StoreTheme.Panel(draw,34,115,202,550,23,StoreTheme.Sidebar);
        T("BROWSE",61,154,4,StoreTheme.Pale);
        for(int i=0;i<Categories.Length;i++)
        {
            float y=200+i*69;
            if(i==menu)
                StoreTheme.Panel(draw,48,y,175,54,12,StoreTheme.Accent,
                    focus==0?StoreTheme.Orange:StoreTheme.Pale);
            T(Categories[i],65,y+17,3.1f,i==menu?StoreTheme.Text:StoreTheme.Pale);
        }

        StoreTheme.Panel(draw,249,115,678,550,22,StoreTheme.Surface);
        if(viewingDetail)RenderDetail();
        else RenderCatalog();

        T(viewingDetail?"A NEXT IMAGE    B BACK    START EXIT"
            :"D PAD NAVIGATE    A SELECT    B BACK    X REFRESH    START EXIT",
            45,687,2.5f,StoreTheme.Pale);
        draw.End();
        device.EndFrame();
    }

    private void RenderPicture(float x,float y,float w,float h)
    {
        if(picture!=null)draw.DrawTexture(picture,new RectF(x,y,w,h),Color32.White);
        else StoreTheme.Placeholder(draw,x,y,w,h);
    }

    private void RenderCatalog()
    {
        T(Categories[menu]+" SOFTWARE",275,142,3.5f,StoreTheme.Pale);
        var feature=Current;
        StoreTheme.Panel(draw,274,177,629,285,17,StoreTheme.Card,
            focus==1?StoreTheme.Orange:StoreTheme.Border);
        RenderPicture(291,191,259,244);
        if(feature==null)
        {
            T(catalogTask==null?"NO SOFTWARE AVAILABLE":"LOADING SOFTWARE",574,239,3.6f,StoreTheme.Text);
            T(Trim(status,36),575,306,2.5f,StoreTheme.Muted);
        }
        else
        {
            T(Trim(feature.Title,22),575,214,5.4f,StoreTheme.Text);
            T(Trim(feature.Category,17)+" / V"+Trim(feature.Version,12),575,263,2.6f,StoreTheme.Pale);
            T("RATING "+feature.AverageRating+" / "+feature.RatingCount,575,313,3.1f,StoreTheme.Orange);
            T(Trim(feature.Description,36),575,360,2.6f,StoreTheme.Text);
            if(feature.Description.Length>36)
                T(Trim(feature.Description.Substring(36),34),575,389,2.6f,StoreTheme.Text);
        }
        T("RECENTLY UPDATED",278,496,3.2f,StoreTheme.Pale);
        for(int i=0;i<3;i++)
        {
            int at=scroll+i;
            float x=274+i*211;
            StoreTheme.Panel(draw,x,526,199,96,11,StoreTheme.Card,
                focus==1&&at==selected?StoreTheme.Orange:StoreTheme.Border);
            if(at<shown.Count)
            {
                T(Trim(shown[at].Title,19),x+14,550,2.8f,StoreTheme.Text);
                T("V"+Trim(shown[at].Version,19),x+14,587,2.5f,StoreTheme.Pale);
            }
        }
        if(shown.Count>3)T((selected+1)+" / "+shown.Count,830,640,2,StoreTheme.Pale);
    }

    private void RenderDetail()
    {
        var item=Current;
        if(item==null)return;
        T("SOFTWARE DETAILS",275,142,3.5f,StoreTheme.Pale);
        StoreTheme.Panel(draw,274,177,629,445,17,StoreTheme.Card);
        RenderPicture(289,191,320,248);
        T(Trim(item.Title,19),629,201,4.8f,StoreTheme.Text);
        T("V"+Trim(item.Version,15),629,249,3.1f,StoreTheme.Pale);
        T(Trim(item.Category,17),629,286,2.6f,StoreTheme.Muted);
        T("RATING "+item.AverageRating+" / "+item.RatingCount,629,330,3,StoreTheme.Orange);
        if(detailTask!=null)T("LOADING DETAILS",629,391,2.6f,StoreTheme.Muted);
        if(detail!=null)
        {
            T("IMAGES "+(detail.ImageIndex+1)+" / "+detail.Screenshots.Count,
                290,448,2.6f,StoreTheme.Pale);
            T("RELEASES "+detail.ReleaseCount,628,394,2.6f,StoreTheme.Pale);
        }
        T("DESCRIPTION",290,496,2.8f,StoreTheme.Muted);
        string desc=item.Description;
        for(int line=0;line<3&&desc.Length>0;line++)
        {
            int length=Math.Min(81,desc.Length);
            T(desc.Substring(0,length),290,523+line*27,2.4f,StoreTheme.Text);
            desc=desc.Substring(length);
        }
    }

    public void Dispose()
    {
        ClearPicture();
        // Outstanding request tasks are intentionally not disposed while in progress;
        // console exits immediately after return from Run.
    }
}
