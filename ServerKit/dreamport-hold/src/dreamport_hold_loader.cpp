/*
 * mod-dreamport-hold - Dreamport ServerKit. The module loader.
 *
 * AzerothCore calls Add<folder name with '-' replaced by '_'>Scripts() for every folder in modules/ that has a src/ folder
 * (modules/CMakeLists.txt 152-158: string(REGEX REPLACE - "_" ...), "Add${LOCALE_SCRIPT_MODULE}Scripts()"; ConfigureModules.cmake
 * GetPathToModuleSource = modules/<name>/src). setup-hold.sh installs this folder as modules/mod-dreamport-hold, so the name is
 * Addmod_dreamport_holdScripts (the same pattern as AzerothCore's skeleton-module, src/MP_loader.cpp).
 */

void AddSC_dreamport_hold();

void Addmod_dreamport_holdScripts()
{
    AddSC_dreamport_hold();
}
