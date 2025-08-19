# AI Generated - Python script for generating mipmaps and tiles from PNG images

import os
import argparse
from PIL import Image
import math

# Increase PIL's decompression bomb limit for large textures
Image.MAX_IMAGE_PIXELS = None

def generate_mipmaps_and_tiles(source_path, min_mip_size=32, tile_size=256):
    """Generate mipmaps and tiles from source image"""
    
    # Load source image
    source_img = Image.open(source_path)
    source_dir = os.path.dirname(source_path)
    source_name = os.path.splitext(os.path.basename(source_path))[0]
    
    # Create main mipmap folder
    mipmap_folder = os.path.join(source_dir, f"{source_name}_Mipmaps")
    os.makedirs(mipmap_folder, exist_ok=True)
    
    # Get source image properties
    original_mode = source_img.mode
    original_format = source_img.format
    
    # Generate mip levels
    current_img = source_img.copy()
    mip_level = 0
    
    while current_img.width >= min_mip_size and current_img.height >= min_mip_size:
        # Create mip level folder
        mip_folder = os.path.join(mipmap_folder, f"Mip_{mip_level}")
        os.makedirs(mip_folder, exist_ok=True)
        
        # Save mipmap
        mip_path = os.path.join(mip_folder, f"mip_{mip_level}.png")
        save_image_with_properties(current_img, mip_path, source_img)
        
        # Generate tiles if image is larger than tile size
        if current_img.width > tile_size or current_img.height > tile_size:
            generate_tiles(current_img, mip_folder, mip_level, tile_size, source_img)
        
        print(f"Generated Mip {mip_level}: {current_img.width}x{current_img.height}")
        
        # Generate next mip level
        mip_level += 1
        next_width = current_img.width // 2
        next_height = current_img.height // 2
        
        if next_width >= min_mip_size and next_height >= min_mip_size:
            current_img = current_img.resize((next_width, next_height), Image.LANCZOS)
        else:
            break
    
    print(f"Generated {mip_level} mip levels in: {mipmap_folder}")

def generate_tiles(image, tiles_folder, mip_level, tile_size, source_img):
    """Generate tiles for a mip level"""
    
    tiles_x = math.ceil(image.width / tile_size)
    tiles_y = math.ceil(image.height / tile_size)
    
    for y in range(tiles_y):
        for x in range(tiles_x):
            start_x = x * tile_size
            start_y = y * tile_size
            end_x = min(start_x + tile_size, image.width)
            end_y = min(start_y + tile_size, image.height)
            
            # Extract tile
            tile = image.crop((start_x, start_y, end_x, end_y))
            
            # Save tile
            tile_path = os.path.join(tiles_folder, f"tile_{mip_level}_{x}_{y}.png")
            save_image_with_properties(tile, tile_path, source_img)

def save_image_with_properties(image, path, source_image):
    """Save image preserving source properties"""
    
    # Preserve format and mode
    if image.mode != source_image.mode:
        image = image.convert(source_image.mode)
    
    # Copy metadata if available
    info = source_image.info.copy()
    
    # Save with original properties
    image.save(path, format='PNG', **info)

def main():
    parser = argparse.ArgumentParser(description='Generate mipmaps and tiles from PNG image')
    parser.add_argument('source', help='Source PNG image path')
    parser.add_argument('--min-mip-size', type=int, default=32, help='Minimum mip size (default: 32)')
    parser.add_argument('--tile-size', type=int, default=256, help='Tile size (default: 256)')
    
    args = parser.parse_args()
    
    if not os.path.exists(args.source):
        print(f"Error: Source file '{args.source}' not found")
        return
    
    if not args.source.lower().endswith('.png'):
        print("Error: Source must be a PNG file")
        return
    
    generate_mipmaps_and_tiles(args.source, args.min_mip_size, args.tile_size)

if __name__ == "__main__":
    main()