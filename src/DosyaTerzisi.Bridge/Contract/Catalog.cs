namespace DosyaTerzisi.Bridge.Contract;

/// <summary>Arayüzün uygulamadan isteyebileceği bir iş: adı, parametre tipi ve cevap tipi.</summary>
/// <param name="NullableResult">Cevap <c>null</c> olabilir (örneğin geri alınacak işlem yokken).</param>
public sealed record MethodSpec(string Name, Type Params, Type Result, bool NullableResult = false);

/// <summary>Arayüz ile uygulama arasındaki bütün istekler. TypeScript tarafındaki <c>Methods</c> arayüzü buradan üretilir.</summary>
public static class Catalog
{
    public static readonly IReadOnlyList<MethodSpec> Methods =
    [
        new("init", typeof(Empty), typeof(InitState)),
        new("pickFolder", typeof(PickFolderParams), typeof(string), NullableResult: true),
        new("scan", typeof(ScanParams), typeof(ScanResult)),
        new("preview", typeof(PreviewParams), typeof(Preview)),
        new("prepareRename", typeof(Empty), typeof(RenameRequest)),
        new("executeRename", typeof(ExecuteRenameParams), typeof(RenameOutcome)),
        new("undo", typeof(Empty), typeof(UndoOutcome), NullableResult: true),
        new("history", typeof(Empty), typeof(List<HistoryItem>)),
        new("addFavorite", typeof(AddFavoriteParams), typeof(List<Favorite>)),
        new("updateFavorite", typeof(UpdateFavoriteParams), typeof(List<Favorite>)),
        new("removeFavorite", typeof(RemoveFavoriteParams), typeof(List<Favorite>)),
        new("moveFavorite", typeof(MoveFavoriteParams), typeof(List<Favorite>)),
        new("importFavorites", typeof(Empty), typeof(ImportResult)),
        new("exportFavorites", typeof(Empty), typeof(ExportResult)),
        new("refresh", typeof(Empty), typeof(ScanResult)),
        new("openPaths", typeof(OpenPathsParams), typeof(OpenPathsResult)),
        new("listFolders", typeof(ListFoldersParams), typeof(FolderList)),
        new("pickFile", typeof(PickFileParams), typeof(string), NullableResult: true),
        new("shellIntegration", typeof(Empty), typeof(ShellIntegrationState)),
        new("setShellIntegration", typeof(SetShellIntegrationParams), typeof(ShellIntegrationState)),
        new("removeRows", typeof(RemoveRowsParams), typeof(ScanResult)),
        new("tagPreview", typeof(TagPreviewParams), typeof(Preview)),
        new("scriptInfo", typeof(Empty), typeof(ScriptInfo)),
        new("editScript", typeof(Empty), typeof(ScriptInfo)),
        new("tagColumns", typeof(TagColumnsParams), typeof(TagColumnValues)),
        new("readTags", typeof(ReadTagsParams), typeof(TagList)),
        new("attributePreview", typeof(AttributePreviewParams), typeof(AttributePreview)),
        new("applyAttributes", typeof(AttributePreviewParams), typeof(AttributeOutcome)),
        new("undoAttributes", typeof(Empty), typeof(AttributeUndo), NullableResult: true),
        new("numberedPreview", typeof(NumberedOptions), typeof(NumberedPlan)),
        new("createNumbered", typeof(NumberedOptions), typeof(CreateOutcome)),
    ];

    public static MethodSpec? Find(string name) => Methods.FirstOrDefault(m => m.Name == name);
}
