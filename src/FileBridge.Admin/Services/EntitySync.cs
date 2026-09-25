namespace FileBridge.Admin.Services;

/// <summary>Keyed sync of a parent's child collection: updates matches by Id, adds new rows (Id == 0), removes the rest.</summary>
public static class EntitySync
{
    public static void Sync<TEntity, TModel>(List<TEntity> existing, List<TModel> incoming, Action<TEntity, TModel> apply, Func<TModel, TEntity> create)
        where TEntity : class
    {
        var idProp = typeof(TEntity).GetProperty("Id")!;
        var modelIdProp = typeof(TModel).GetProperty("Id");
        var existingById = existing.ToDictionary(e => (int)idProp.GetValue(e)!);
        var keepIds = new HashSet<int>();

        foreach (var s in incoming)
        {
            var id = modelIdProp is null ? 0 : (int)(modelIdProp.GetValue(s) ?? 0);
            if (id != 0 && existingById.TryGetValue(id, out var entity)) { apply(entity, s); keepIds.Add(id); }
            else { var e = create(s); apply(e, s); existing.Add(e); }
        }
        existing.RemoveAll(e => (int)idProp.GetValue(e)! != 0 && !keepIds.Contains((int)idProp.GetValue(e)!));
    }
}
