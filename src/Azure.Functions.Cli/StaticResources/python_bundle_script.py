import os
import sys

from PyInstaller.__main__ import run
from distutils.sysconfig import get_python_lib


# Gets the list of all the possible modules from a directory
def get_possible_modules(location, parent = ''):
    all_modules = []
    dirs, files = get_dirs_files(location)
    for a_file in files:
        if is_python_file(a_file):
            all_modules.append(parent + a_file.split(os.extsep)[0])
    for a_dir in dirs:
        if dir_has_python_files(os.path.join(location, a_dir)):
            all_modules.append(parent + a_dir)
        # Now we recurse through the sub_directory to get all the possible modules and append it to the parent list
        all_modules.extend(get_possible_modules(os.path.join(location, a_dir), parent + a_dir + '.'))
    return all_modules

# Gets the files and directories from a location
def get_dirs_files(location):
    dir_entries = os.listdir(location)
    files = [entry for entry in dir_entries if os.path.isfile(os.path.join(location, entry))]
    dirs = [entry for entry in dir_entries if os.path.isdir(os.path.join(location, entry))]
    return dirs, files

# Checks if the directory has any python files in it
def dir_has_python_files(a_dir):
    dirs, files = get_dirs_files(a_dir)
    for a_file in files:
        if is_python_file(a_file):
            return True
    for a_sub_dir in dirs:
        if dir_has_python_files(os.path.join(a_dir, a_sub_dir)):
            return True
    return False

def is_python_file(a_file):
    return a_file.endswith('.so') or a_file.endswith('.py') or a_file.endswith('.pyd') or a_file.endswith('.pyo') or a_file.endswith('.pyc')

def __main__():
    if len(sys.argv) < 2:
        print("Need more arguments: Usage: python_build_template.py <starter_script> [woker_python_packages]")
        return 
    packages_location = get_python_lib()
    all_modules = get_possible_modules(packages_location)
    paths_var = packages_location
    if len(sys.argv) >= 3:
        woker_python_packages = sys.argv[2]
        all_modules.extend(get_possible_modules(woker_python_packages))
        paths_var = paths_var + ':' + woker_python_packages
    entry_file = sys.argv[1]
    # Creates a list of hidden imports arguments for pyinstaller in the format - "--hidden-import=<module>"
    hidden_modules_commands = ["--hidden-import=" + module_name for module_name in all_modules]
    run(["--paths=" + paths_var, "--distpath=.", *hidden_modules_commands, "--name=worker-bundle", entry_file])

if __name__ == '__main__':
    __main__()