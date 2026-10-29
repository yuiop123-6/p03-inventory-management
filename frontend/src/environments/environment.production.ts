/**
 * 生产环境配置（ng build --configuration production 时通过 fileReplacements 替换）。
 * apiUrl 为指定的后端地址入口：部署时按实际后端地址修改此处，
 * 或由反向代理把 /api 转发到后端后改回 '/api'。
 */
export const environment = {
  production: true,
  apiUrl: 'http://localhost:5080/api',
};
