namespace LibTreeMapView.Core.Coff;

/// <summary>.lib ファイルとして解釈できなかったときに投げられる。</summary>
public sealed class LibFormatException : Exception
{
    public LibFormatException(string message) : base(message)
    {
    }

    public LibFormatException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
