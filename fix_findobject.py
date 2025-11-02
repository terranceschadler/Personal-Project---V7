#!/usr/bin/env python3
"""
Unity FindObject Deprecation Fixer
Automatically updates deprecated FindObjectOfType and FindObjectsOfType calls
to the new Unity API methods.
"""

import re
import os
from pathlib import Path

# Define replacement patterns
REPLACEMENTS = [
    # FindObjectOfType<T>() -> FindFirstObjectByType<T>()
    (
        r'FindObjectOfType<([^>]+)>\(\)',
        r'FindFirstObjectByType<\1>()'
    ),
    
    # FindObjectOfType<T>(true) -> FindFirstObjectByType<T>(FindObjectsInactive.Include)
    (
        r'FindObjectOfType<([^>]+)>\(true\)',
        r'FindFirstObjectByType<\1>(FindObjectsInactive.Include)'
    ),
    
    # FindObjectOfType<T>(false) -> FindFirstObjectByType<T>()
    (
        r'FindObjectOfType<([^>]+)>\(false\)',
        r'FindFirstObjectByType<\1>()'
    ),
    
    # FindObjectOfType(typeof(...)) -> FindFirstObjectByType(typeof(...))
    (
        r'FindObjectOfType\(typeof\(([^)]+)\)\)',
        r'FindFirstObjectByType(typeof(\1))'
    ),
    
    # FindObjectsOfType<T>() -> FindObjectsByType<T>(FindObjectsSortMode.None)
    (
        r'FindObjectsOfType<([^>]+)>\(\)',
        r'FindObjectsByType<\1>(FindObjectsSortMode.None)'
    ),
    
    # FindObjectsOfType<T>(true) -> FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)
    (
        r'FindObjectsOfType<([^>]+)>\(true\)',
        r'FindObjectsByType<\1>(FindObjectsInactive.Include, FindObjectsSortMode.None)'
    ),
    
    # FindObjectsOfType<T>(false) -> FindObjectsByType<T>(FindObjectsSortMode.None)
    (
        r'FindObjectsOfType<([^>]+)>\(false\)',
        r'FindObjectsByType<\1>(FindObjectsSortMode.None)'
    ),
]

def fix_file(filepath):
    """Fix deprecated FindObject calls in a single file."""
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()
        
        original_content = content
        changes_made = 0
        
        # Apply all replacements
        for pattern, replacement in REPLACEMENTS:
            new_content = re.sub(pattern, replacement, content)
            if new_content != content:
                changes_made += content.count(pattern.replace(r'\(', '(').replace(r'\)', ')'))
                content = new_content
        
        # Only write if changes were made
        if content != original_content:
            with open(filepath, 'w', encoding='utf-8') as f:
                f.write(content)
            return changes_made
        
        return 0
    
    except Exception as e:
        print(f"Error processing {filepath}: {e}")
        return 0

def fix_directory(directory):
    """Fix all C# files in a directory recursively."""
    directory = Path(directory)
    total_files = 0
    total_changes = 0
    
    # Find all .cs files
    cs_files = list(directory.rglob('*.cs'))
    
    print(f"Found {len(cs_files)} C# files to process...")
    print()
    
    for filepath in cs_files:
        changes = fix_file(filepath)
        if changes > 0:
            total_files += 1
            total_changes += changes
            print(f"✓ Fixed {changes} instance(s) in: {filepath.relative_to(directory)}")
    
    print()
    print(f"Summary:")
    print(f"  Files modified: {total_files}")
    print(f"  Total changes: {total_changes}")
    
    if total_changes == 0:
        print("  No deprecated FindObject calls found!")

def main():
    import sys
    
    print("Unity FindObject Deprecation Fixer")
    print("=" * 50)
    print()
    
    if len(sys.argv) < 2:
        print("Usage: python fix_findobject.py <path_to_Assets_folder>")
        print()
        print("Example:")
        print("  python fix_findobject.py Assets/Scripts")
        print("  python fix_findobject.py .")
        sys.exit(1)
    
    target_path = sys.argv[1]
    
    if not os.path.exists(target_path):
        print(f"Error: Path '{target_path}' does not exist!")
        sys.exit(1)
    
    print(f"Scanning: {target_path}")
    print()
    
    fix_directory(target_path)
    
    print()
    print("Done! Remember to test your project after these changes.")
    print()
    print("Note: This script converts:")
    print("  - FindObjectOfType → FindFirstObjectByType")
    print("  - FindObjectsOfType → FindObjectsByType (with FindObjectsSortMode.None)")

if __name__ == "__main__":
    main()
