﻿using CollectionsOnline.Core.Models;
using IMu;

namespace CollectionsOnline.Import.Factories
{
    public interface IMuseumLocationFactory
    {
        MuseumLocation Make(Map map);
    }
}