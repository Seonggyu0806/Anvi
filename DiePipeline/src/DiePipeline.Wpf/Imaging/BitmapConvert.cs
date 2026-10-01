using System.IO;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace DiePipeline.Wpf.Imaging;

/// <summary>칠판의 중간 이미지(<see cref="Mat"/>) → 화면이 그릴 수 있는 그림.</summary>
public static class BitmapConvert
{
    public static BitmapSource ToBitmap(Mat mat)
    {
        ArgumentNullException.ThrowIfNull(mat);

        Mat display = mat;
        bool temporary = false;

        // ★ 작업 포맷은 GrayF32 [0,1] 이다. 화면은 0~255 를 기대하므로 255를 곱해 8비트로 바꾼다.
        //   안 하면 0.4 가 0 으로 잘려서 <b>전부 새까맣게</b> 보인다.
        if (mat.Type().Depth == MatType.CV_32F)
        {
            display = new Mat();
            mat.ConvertTo(display, MatType.CV_8U, 255.0);
            temporary = true;
        }

        try
        {
            Cv2.ImEncode(".png", display, out byte[] png);

            using MemoryStream stream = new(png);

            BitmapImage bitmap = new();
            bitmap.BeginInit();
            bitmap.StreamSource = stream;

            // ★ OnLoad — 스트림을 지금 다 읽는다. 기본값은 게으르게 읽어서,
            //   using 으로 스트림이 닫힌 뒤에 읽으려다 터진다.
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();

            // ★ Freeze — 더 못 바꾸게 얼린다. 그래야 다른 스레드에서 만들어 UI 로 넘겨도 안전하다.
            //   나중에 미리보기를 백그라운드로 돌릴 때 이 한 줄이 값을 한다.
            bitmap.Freeze();

            return bitmap;
        }
        finally
        {
            // ★ 우리가 만든 것만 놓는다. 원본은 칠판 것이라 건드리면 안 된다.
            if (temporary)
            {
                display.Dispose();
            }
        }
    }
}