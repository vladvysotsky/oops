using Xunit;

// Тесты не гоняются параллельно: LayoutConverter.SmartWordSelection —
// статическая настройка, и класс, который её переключает, иначе влиял бы на
// соседние классы посреди их прогона. Вся сборка проходит за секунду, так что
// терять на этом нечего.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
