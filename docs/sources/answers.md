Ronda 1

Contexto
1. ¿Qué pueblo es y qué fiestas son (fechas y actos principales)? Te lo pregunto porque cada sitio tiene sus propias costumbres: si hay alardo, embajadas, guerrillas…
San Vicente del Raspeig. Fiestas de Moros y Cristianos. Tiene una embajada Mora, una embajada Cristiana, una diana Mora y una diana Mora. En las dianas se realiza el concurso de Disparo.
2. ¿Cuántas comparsas hay y cuántos arcabuceros aproximadamente, en total y por comparsa?
Hay 20 actualmente, y cada comparsa tiene un numero variable de arcabuceros, desde 10 hasta 60 aprox.
3. ¿Cuál es tu papel? ¿Eres de una comparsa, de la Junta/Asociación de Fiestas, responsable de pólvora…?
Jefe de Disparo de una Comparsa que siente que se pueden organizar mejor las cosas

El problema
4. ¿Qué gestionan hoy esos Excels? ¿Quién los mantiene y qué es lo que más duele: errores, duplicados, versiones distintas, tiempo perdido, papeles para la Guardia Civil…?
Hay varios excels, los que mantienen cada comparsa de sus arcabuceros, con estructura variable y son internos, y los que maneja la Unión. A nosotros en la comparsa nos duele el mantenimiento y actualizacion de la informacion, y a la Unión la uniformidad y que los datos sean correcto. A partir de estos excels, que se generan de cada comparsa, se nos evian para validar. Cuando se acercan fiestas nos abren un formulario de google drive para insertar los datos de cada arcabucero y la cantidad de polvora a pedir (1 o 2 kg), si alquilan cantimplora/polvorera y si alquilan arma ese año o la tienen en propiedad. Luego la union con todo esto ya genera unos excels globales que envian a proveedores de polvora, armas y a la intervencion de armas del gobierno central y demas para que autoricen los actos de fiestas. Tambien se gestionan cesiones de armas, y nosotros internamente si son necesarios pistones y cantidades, asi como su tipo grande o pequeño de pistola
5. Si PolvorApp funcionara perfectamente dentro de un año, ¿qué 3 cosas habrían cambiado?
Unica fuente de verdad, eliminacion de envio de excels y formularios ineficientes, portal unico de gestion.

Alcance y usuarios
6. ¿Para quién es la app? ¿Para una sola comparsa, para todas (gestionada por la Junta) o para las dos cosas (multi-comparsa con permisos)?
Tanto para las personas de la unión, como para los jefes de disparo.
7. ¿Quién la usaría? Por ejemplo: administrador de la Junta, responsable de pólvora de cada comparsa, cabos de escuadra, el propio arcabucero consultando sus datos, la Guardia Civil o la intervención de armas como destinatarios de informes…
Responsables de la union de fiestas y jefes de disparo de cada comparsa
8. ¿El arcabucero tendría acceso propio (ver su carnet, su pólvora asignada, inscribirse) o solo lo gestionan otros?
El arcabucero no tendria acceso, la aplicacion es para la gestion.

Dominio (pólvora y arcabuces)
9. ¿Qué se controla de cada arcabucero? Por ejemplo: carnet de CRE (Consumidor Reconocido como Experto) y su caducidad, curso de formación, seguro, edad o menores, arcabuz propio o de la comparsa…
Licencia (caducidad), curso de formacion obligatorio, mayor de edad, arma propia o alquileres por eventos, numero de Kg de polvora por evento, alquieler por evento de cantimplora/polvorera. Actualmente, en una app externa, que gestiona la union y solamente tienen acceso secretaria de cada comparsa, tienen el listado oficial de comparsistas. Alli es donde se actualiza las fechas de licencia, si son arcabuceros o no, y la subida de la foto licencia por delante y por detras, asi como la foto obligatoria de carne. Esta peinso que seria adecuado dejarla en desuso para el tema de arcabuceria y empezar a usar esta nueva aplicacion. Es obligatorio que todos los arcabuceros hayan realizado el curso obligatorio antes de disparar. La union tambien organiza estos cursos.
10. ¿Cómo funciona la pólvora? Quién la compra, cómo se reparte (kg por persona y acto), si hay que registrar consumos o devoluciones, y qué límites legales os aplican.
Las comparsas, a través del Jefe de disparo, manda un listado a la union con el pedido por persona de su comparsa. La union la aglutina en un unico pedido al proveedor. Cuando son fiestas, todas las comparsas y la union, junto con la guardia civil quedan en un punto alejado del pueblo para realizar la entrega de la polvora y su cantimplora de manera ordenada y por turnos de comparsa.
11. ¿Se gestionan también los arcabuces como inventario (número de serie, revisiones, propietario)?
La union tambien recepciona los arcabuces por parte de la empresa que alquila y un dia cerca de fiestas nos establecen un dia para asistir y repartirlos por turnos de comparsa.

Documentación y fuentes
12. ¿Me puedes pasar los Excels? Déjalos en una carpeta como polvorapp/docs/fuentes/. Importante: si tienen datos personales reales (DNI, teléfonos…), mejor una copia anonimizada o con unas pocas filas inventadas. Lo que me interesa es la estructura: columnas, hojas y fórmulas.
Te los dejo ahi
13. ¿Tienes circulares, normativa, formularios de la Guardia Civil o bases de la Junta que sigáis ahora mismo? También me sirven.
Te dejo algunas circulares.

Una pregunta práctica
14. Cuando dices ECC, ¿te refieres al plugin Everything Claude Code? ¿Y qué versión de OpenSpec tienes instalada, o la instalamos al final de estas rondas? Así adapto la estructura de los documentos a lo que espera cada herramienta.
https://github.com/affaan-m/ECC e instala openspec, pero esto ya mas tarde. De momento solo la documentacion para permitir un mejor desarrollo.

Notas:
Todo el codigo y artefactos del repo serán en ingles y la comunicacion conmigo en español. La aplicacion final debera soportar Español, Valenciano e Ingles.

------------------------------

Ronda 2: dominio y datos

No hace falta que contestes todo. Las dos primeras son las más importantes, porque cambian el enfoque del MVP.

Estrategia
1. ¿La Unión está de acuerdo con el proyecto? ¿El MVP debe servir desde el principio a la Unión y las 20 comparsas, o empezamos con una sola comparsa (la tuya) y luego lo ampliamos?
La union no lo sabe, es una propuesta mia ya que nuestra comparsa gestiona este año 56 persona y es muy tedioso. Lo empezare a probar yo como Jefe de disparo pero peinso que podemos extender a 20 comparsas como MVP
2. ¿Quién alojaría y pagaría la app, y quién sería el responsable de los datos a efectos del RGPD? Lo lógico sería la Unión.
La union

Licencias y personas

3. ¿Qué significan las licencias AE y A-PROF? ¿Siempre duran 5 años? ¿Existen otros tipos?
La clicencia AE es la de Abancarga. La otra es la profesional que tiene gente como policias, etc.
4. ¿La app debe guardar las fotos de la licencia (delante y detrás) y la foto de carnet, sustituyendo a la app externa?
Si
5. ¿La app externa de la Unión puede exportar datos para hacer una carga inicial? ¿Seguirá siendo la fuente oficial para los comparsistas que no son arcabuceros?
LA otra app sera la fuente oficial para otros temas, pero polvorapp sera la oficial para arcabuceria. Se tiene que mantener la referencia con el id de union (que es justamente el id del registro en la otra app)
6. ¿Puede haber varios jefes de disparo por comparsa? En la hoja de notas veo dos autores. ¿Qué roles tendría la Unión: administrador, área de fiestas…?
Si, pueden haber varios. La union creo que podemos simplificar a administradores solo.

Armas

7. ¿El catálogo de armas completo es: bando × diestro/zurdo × normal/pequeño, más la pistola? ¿Las comparsas moras usan "Arcabuz Moro"?
El alquiler de armas no suele cambiar y son bando x diestro/zurdo x tamaño. Pistola no se ofrece en alquiler. Son trabucos cristianos y arcabuces moros. Cada año puede variar si estan o no disponibles algunos modelos.
8. ¿Quién asigna el número de arma (por ejemplo 37-17)? ¿Hay que asignar un arma concreta a cada persona? ¿Se controla la devolución después de fiestas?
El numero de arma viene en la culata de cada una, y en el caso del alquiler, las acuña el proveedor. Si, cada arma esta asignada a una persona en particular e instrasferible. Si, al finalizar las fiestas, lñas armas se entregan al proveedor y este valida que esten todas.
9. Sobre la cesión de arma: ¿tiene que ser entre personas de la misma comparsa? ¿Hay algún límite?
No tiene porque ser de la misma comparsa, aunque creo que nunca se ha dado el caso. No hay limite.

Pólvora y material

10. ¿El pedido es de 0, 1 o 2 kg por persona y por año, o por acto? En la ronda 1 dijiste "por evento".
El pedido es por año. 
11. ¿Cómo funcionan la pólvora remanente, la de comparsa frente a la extra y el precio por kg? ¿Quién paga qué? ¿Entra en la app?
Nunca hay remantene, la polvora que sobra se envvia al polvorin para su destruccion. No se almacena en ninguna comparsa. Los comparsistas pagan su cuota a la comparsa, y la comparsa realiza la transferencia conjunta de sus arcabuceros a la union.
12. ¿Qué se apunta en Trazabilidad 1 y 2 el día del reparto?
Lo desconozco
13. ¿Los pistones son solo un asunto interno de la comparsa, o también se piden a través de la Unión?
Se piden a la Union
14. ¿Qué significan Reserva, Recarga y Salvas patrón? ¿El almuerzo entra en la app?
Son terminos internos. Las Salvas al Patrón es otro acto menor que no te nombré. El almuerzo es algo externo nuestro solo, no entra. Reserva es gente nuestra interna que este año no participa, pero la mantenemos en el lsitado por temas de gestion y con 0 KG de polvora, por si fuera necesario llamarlas para el dia de la recogida de polvora y que se la puedan recoger a otras personas. Si no esta en lsitado, no puedes ser los autorizados.

Flujo y salidas

15. ¿Qué pasa con el pedido una vez enviado? Por ejemplo: borrador → enviado → validado o devuelto con correcciones → cerrado. ¿La app debe bloquear cambios al pasar la fecha límite?
Si, los administradores o union deben de poder bloquear las ediciones de cada año, asi como el registro general.
16. ¿Puedes conseguir plantillas anonimizadas de lo que la Unión envía al proveedor de pólvora, a la empresa de armas y a Intervención de Armas? Es clave para diseñar las exportaciones.
De momento no, pero es algo que mas adelante igual puedo.
17. ¿Queremos gestionar en la app los días de reparto (turnos, lista de entrega, firmas)? ¿Hay cobertura móvil en el lugar donde se entrega la pólvora?
Puede no existir cobertura, o tener mala conexion. Seria ideal si se pudiera gestionar este tipo de operativa.

Varios

18. ¿La mayoría de edad se comprueba a fecha del pedido o a fecha de fiestas?
Es responsabilidad de cada jefe de disparo que los datos y requisitos de sus arcabuceros se cumplan
19. Sobre los cursos: ¿basta con marcar "curso hecho" y la fecha, o la app debe gestionar sesiones e inscripciones?
De momento solamente con curso realizado y la fecha. 
20. ¿El concurso de disparo entra en el alcance?
Creo que no seria necesario, es otro evento menor. Mas adelanto podemos plantearlo.
21. ¿Quieres avisos por email a los jefes de disparo (caducidades, fechas límite)?
Estaria bien poder gestionarlas.
22. ¿Tienes una fecha objetivo? Por ejemplo, tener algo usable antes de las fechas límite de licencias y pedidos para las fiestas de 2027.
2027 o 2028

Ronda 3: casos de uso

23. ¿Estás de acuerdo con el reparto por fases? ¿Moverías algo de sitio o falta algún caso de uso?
Estoy de acuerdo
24. ¿Puedes copiarme las preguntas exactas del formulario de Google de la Unión? ¿Se envía una respuesta por arcabucero o una por comparsa? Es lo que necesito para que la exportación del piloto encaje con lo que pide la Unión.
Es exactamente como el excel donde se almacena los KG de polvora, el numeor de Guia de tpertenencia, las censiones, alquieres, etc. El formulario de google es unico por arcabucero, de ahi que yo tenga que hacerlo 56 veces y este sistema lo reemplazaria.
25. ¿Un jefe de disparo puede corregir el pedido después de enviarlo, si aún no ha pasado la fecha límite? ¿Y cómo se gestionan hoy los cambios de última hora?
Actualmente puede corregirlo, ya que la union habilita un periodo de correciones. Para los casos excepcionales, hablamos con ellos personalemtne.
26. En el reparto, ¿quién apunta las entregas (alguien de la Unión, los jefes de disparo o ambos) y con qué dispositivo? ¿Firma el arcabucero al recoger?
La union hace el registro de la id de la cantimplora y a que persona se la asigna. lo hacen con un portatil y un excel. No firma el arcabucero ni nadie. El jefe de disparo valida la identidad de su gente.
27. Sobre la autorización de recogida: ¿seguimos con papel firmado (la app genera el PDF ya relleno) o te gustaría una firma digital allí mismo?
Papel
28. ¿La licencia A-PROF también dura 5 años? ¿La licencia tiene un número que haya que guardar? ¿Guardamos el historial de licencias anteriores?
La A-PROF creo que se renueva cada año. No hace falta guardar. 
29. ¿Qué significa Recarga? ¿Entra en la app?
No hace falta.
30. ¿Un arcabucero puede cambiar de comparsa de un año a otro?
Si
31. Cuando alguien se da de alta como arcabucero, ¿siempre tiene ya su ID Unión?
Si
32. ¿Te serviría ver cuánto debe pagar tu comparsa a la Unión (kg × precio + alquileres + pistones)? ¿O el dinero queda fuera de la app?
Estaría muy bien
33. ¿El género es necesario? Solo lo veo en las estadísticas, y el RGPD pide no guardar datos que no se usen.
Estaría bien para reportes de igualdad
34. ¿Las cantimploras de alquiler tienen número y se asignan a una persona, igual que las armas? ¿Se devuelven?
Si, y tambien se devuelven al acabar los actos. Tanto la recogida de armas, como la recogida de cantimploras la hacen el proveedor, la union no las gestiona 

Ronda 4: 

35. Precios: ¿qué se cobra cada año? La pólvora por kg, la caja de pistones (¿cambia según el tipo?), el alquiler de arma (¿según el modelo o un precio fijo?) y el alquiler de cantimplora (¿según el tamaño?). Lo necesito para el resumen de pago.
La polvora, los pistones y los alquileres suelen ser precio fijo.
36. ¿La Unión tiene asesor o delegado de protección de datos? ¿Los arcabuceros firman hoy algún documento de protección de datos?
Tendra que tener delegado, aunque lo desconozco. Actualmente creo que se firma al darse de alta en la Union como comparsista
37. Cuando un arcabucero se da de baja, ¿cuántos años se guardan sus datos?
Si solo se da de baja de disparar, lo solemos almacenar internamente en cada comparsa, y asignamos como 0KG unos años por si acaso y como Inactivo. En caso de darse de baja de la Union, entonces si lo eliminamos.
38. Acceso a la app: ¿email y contraseña con verificación en dos pasos, o un enlace que llega por email sin contraseña? Propongo que la verificación en dos pasos sea obligatoria para los administradores.
Email y contraseña con verificacion en dos pasos. 
39. Tu perfil técnico: ¿qué lenguajes y frameworks conoces o prefieres? ¿Quién mantendrá la app a largo plazo, tú solo? Es lo que más va a pesar al elegir las tecnologías.
Controlo, .NET, Angular, React, Node . De momento la mantendré yo.
40. ¿Cuánto podría pagar la Unión al mes por el alojamiento? ¿Tiene ya dominio web, Google Workspace o algún hosting?
Entiendo que tendrá un hosting ya puesto que tiene la otra app alli, pero nosotros haremos inicialmente un desarrollo local con Docker y un despliegue en contendores de momento. Mas adelante adaptaremos.
41. ¿El repositorio será público (código abierto) o privado?
Sera publico.
42. ¿Hay requisitos para las fotos, como formato o tamaño para imprimir carnets?
Si, estas fotos se usan luego para la emision de unos carnets de arcabuceria que se le entregan a cada arcabucero y se cuelgan al cuello durante los actos que los identifican.


Ronda 5

1. Carnets: ¿quién los imprime hoy y con qué herramienta? ¿Quieres que PolvorApp genere el PDF? ¿Qué datos llevan (foto, nombre, comparsa, licencia, año, algún código QR…)? Si puedes, pásame una foto de un carnet con los datos tapados.
Los imprime la union y desconozco como lo ahcen, pero no sera muy sofisticada la herramienta y estaran hechos a mano digitalmente. te he puesto una foto carnet.jpeg con el mio actual.
2. Decisiones de arquitectura: ¿estás de acuerdo con las 8? ¿Cambiarías algo?
Creo que podemos usar React, será mas liviano y flexible para que hagas una arquitectura adecuada. react + tailwind + shadcn/ui
3. Licencia del código:
   - MIT: cualquiera puede reutilizar el código sin obligaciones.

   Para un proyecto así, que otras uniones de fiestas podrían reutilizar, recomendaría AGPL, pero es decisión tuya.
4. Plan del MVP: ¿te encaja el orden de mvp.md?
Si