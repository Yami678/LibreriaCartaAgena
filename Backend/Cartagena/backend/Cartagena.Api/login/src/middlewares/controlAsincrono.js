export function controlAsincrono(capa) {
  return (req, res, next) => {
    Promise.resolve(capa(req, res, next)).catch(next);
  };
}
