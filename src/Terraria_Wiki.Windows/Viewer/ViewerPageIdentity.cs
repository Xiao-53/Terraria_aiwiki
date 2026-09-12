namespace Terraria_Wiki.Windows.Viewer;

internal sealed record ViewerPageIdentity(
    long Generation,
    int WikiId,
    string WikiDisplayTitle,
    string CanonicalTitle,
    long NavigationVersion);
