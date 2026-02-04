-- Make CollectionId nullable in Assets table
ALTER TABLE "Assets" ALTER COLUMN "CollectionId" DROP NOT NULL;

-- Update the FK to use SET NULL on delete
ALTER TABLE "Assets" DROP CONSTRAINT IF EXISTS "FK_Assets_Collections_CollectionId";
ALTER TABLE "Assets" ADD CONSTRAINT "FK_Assets_Collections_CollectionId" 
    FOREIGN KEY ("CollectionId") REFERENCES "Collections"("Id") ON DELETE SET NULL;

-- Record the migration as applied
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") 
VALUES ('20260204195421_MakeAssetCollectionIdNullable', '9.0.0')
ON CONFLICT DO NOTHING;
