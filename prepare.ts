async function bakeData() {
  const data = await Bun.file('AutopelagoDefinitions.yml').text();
  const defs = Bun.YAML.parse(data);
  await Bun.write('src/app/data/baked.json', JSON.stringify(defs));
}

await Promise.all([
  bakeData(),
]);

export { }
